using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PropertyLedger.Web.Services;

/// <summary>
/// 应用内一键升级（Docker Compose 部署场景，Portainer 同款 companion 方案）：
/// 通过挂载进容器的 /var/run/docker.sock 调用 Docker Engine API，从自身容器的
/// Compose 标签（project / working_dir / config_files / service）还原宿主机部署信息，
/// 创建一次性「升级容器」（docker:cli 镜像，挂载 docker.sock 与 compose 项目目录），
/// 由它执行 `docker compose pull web` + `docker compose up -d web` 拉取新镜像并重建 web 容器。
/// 容器重建期间应用短暂离线，前端轮询匿名 /healthz 端点，版本号变化即升级成功。
/// </summary>
public class DockerUpgradeService
{
    private const string SockPath = "/var/run/docker.sock";
    private const string UpgraderContainer = "propertyledger-upgrader";
    private const string CliImage = "docker:cli";

    private readonly ILogger<DockerUpgradeService> _logger;
    private HttpClient? _http;
    private HttpClient? _pullHttp;

    /// <summary>Linux 容器内且挂载了 docker.sock 时才支持自动升级。</summary>
    public bool CanAutoUpgrade { get; } = OperatingSystem.IsLinux() && File.Exists(SockPath);

    public DockerUpgradeService(ILogger<DockerUpgradeService> logger) => _logger = logger;

    private HttpClient Http => _http ??= new HttpClient(new SocketsHttpHandler
    {
        ConnectCallback = async (ctx, ct) =>
        {
            var sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
            await sock.ConnectAsync(new UnixDomainSocketEndPoint(SockPath), ct);
            return new NetworkStream(sock, ownsSocket: true);
        },
    })
    { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>镜像拉取可能较慢（首次拉 docker:cli），单独长超时客户端。</summary>
    private HttpClient PullHttp => _pullHttp ??= new HttpClient(new SocketsHttpHandler
    {
        ConnectCallback = async (ctx, ct) =>
        {
            var sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
            await sock.ConnectAsync(new UnixDomainSocketEndPoint(SockPath), ct);
            return new NetworkStream(sock, ownsSocket: true);
        },
    })
    { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>启动升级容器（立即返回；后续 pull + 重建由升级容器在后台执行）。</summary>
    public async Task<(bool Started, string Message)> StartUpgradeAsync(CancellationToken ct = default)
    {
        if (!CanAutoUpgrade)
            return (false, "当前环境未挂载 docker.sock，无法自动升级；请在部署机 docker-compose.yml 所在目录手动执行命令。");

        try
        {
            // 1) 自省：HOSTNAME 即容器 ID，读取自身 Compose 标签与挂载
            var selfId = Environment.GetEnvironmentVariable("HOSTNAME");
            if (string.IsNullOrWhiteSpace(selfId)) selfId = Dns.GetHostName();

            using var self = await Http.GetAsync($"/containers/{Uri.EscapeDataString(selfId)}/json", ct);
            if (!self.IsSuccessStatusCode)
                return (false, "无法读取自身容器信息（该容器可能不是 Docker Compose 部署）；请手动执行命令升级。");

            using var doc = JsonDocument.Parse(await self.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            string? project = null, service = null, workdir = null, configFiles = null, sockSource = null;
            if (root.TryGetProperty("Config", out var cfg) && cfg.TryGetProperty("Labels", out var labels) && labels.ValueKind == JsonValueKind.Object)
            {
                project = GetStr(labels, "com.docker.compose.project");
                service = GetStr(labels, "com.docker.compose.service");
                workdir = GetStr(labels, "com.docker.compose.project.working_dir");
                configFiles = GetStr(labels, "com.docker.compose.project.config_files");
            }
            if (root.TryGetProperty("Mounts", out var mounts) && mounts.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in mounts.EnumerateArray())
                {
                    if (GetStr(m, "Destination") == SockPath)
                    {
                        sockSource = GetStr(m, "Source") ?? SockPath;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(project) || string.IsNullOrEmpty(workdir))
                return (false, "该容器不是由 Docker Compose 创建（缺少 Compose 标签），无法自动升级；请手动执行命令。");
            if (string.IsNullOrEmpty(sockSource))
                return (false, "未找到 docker.sock 挂载，无法自动升级；请在 compose 中为 web 服务挂载 /var/run/docker.sock 后重新部署。");

            var configHostPath = (configFiles ?? string.Empty)
                .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrEmpty(configHostPath))
                return (false, "无法定位 docker-compose.yml 路径；请手动执行命令升级。");

            // compose 文件与 .env 都在宿主机项目目录内 → 整个目录只读挂载到升级容器的 /compose
            var configArg = "/compose/docker-compose.yml";
            if (!configHostPath.StartsWith(workdir.TrimEnd('/') + "/", StringComparison.Ordinal))
                return (false, $"compose 文件（{configHostPath}）不在项目目录（{workdir}）内，无法自动升级；请手动执行命令。");

            var sh = $"docker compose --project-directory /compose -p {project} -f {configArg} pull {service}; " +
                     $"docker compose --project-directory /compose -p {project} -f {configArg} up -d {service}";

            var createBody = JsonSerializer.Serialize(new
            {
                Image = CliImage,
                Cmd = new[] { "sh", "-c", sh },
                Labels = new Dictionary<string, string> { ["com.propertyledger.role"] = "upgrader" },
                HostConfig = new
                {
                    Binds = new[] { $"{sockSource}:{SockPath}", $"{workdir}:/compose:ro" },
                },
            });
            _logger.LogInformation("准备在线升级：project={Project} service={Service} workdir={Workdir}", project, service, workdir);

            // 2) 清理上一次升级遗留的容器（忽略 404）
            await Http.DeleteAsync($"/containers/{UpgraderContainer}?force=true&v=true", ct);

            // 3) 创建升级容器；首次部署可能还没拉过 docker:cli 镜像 → 先拉镜像再重试一次
            string? id = null;
            for (var attempt = 0; attempt < 2 && id is null; attempt++)
            {
                using var resp = await Http.PostAsync(
                    $"/containers/create?name={UpgraderContainer}",
                    new StringContent(createBody, Encoding.UTF8, "application/json"), ct);
                if (resp.IsSuccessStatusCode)
                {
                    using var d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                    id = d.RootElement.GetProperty("Id").GetString();
                }
                else if (attempt == 0)
                {
                    _logger.LogInformation("创建升级容器失败（HTTP {Status}），尝试先拉取镜像 {Image} 再重试", resp.StatusCode, CliImage);
                    await PullCliImageAsync(ct);
                }
                else
                {
                    var err = await resp.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("创建升级容器失败：{Err}", err);
                    return (false, "创建升级容器失败（" + resp.StatusCode + "）；请手动执行命令升级。");
                }
            }

            // 4) 启动 → 升级容器在后台执行 pull + up -d，重建 web 容器
            using var start = await Http.PostAsync($"/containers/{id}/start", null, ct);
            if (!start.IsSuccessStatusCode)
            {
                _logger.LogWarning("升级容器启动失败：HTTP {Status}", start.StatusCode);
                return (false, "升级容器启动失败；请手动执行命令升级。");
            }
            return (true, "升级容器已启动，正在拉取新镜像并重建 web 容器。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动在线升级失败");
            return (false, "启动升级失败：" + ex.Message);
        }
    }

    private async Task PullCliImageAsync(CancellationToken ct)
    {
        using var resp = await PullHttp.PostAsync($"/images/create?fromImage=docker&tag=cli", null, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"拉取 {CliImage} 镜像失败（{resp.StatusCode}）：{err}");
        }
        // 响应为进度流，读完即拉取完成
        await resp.Content.CopyToAsync(Stream.Null, ct);
    }

    private static string? GetStr(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}
