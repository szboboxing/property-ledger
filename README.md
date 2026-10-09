# 物业管理小工具 Property Ledger

房东记账工具：物业 → 房间 → 租客 → 租约 → 按月/季自动出账，支持自定义杂费（水电/物业/网费）、部分收款自动算剩余，一键生成中文账单图片（含收款二维码）用微信发给租客。

A landlord's ledger: properties → rooms → tenants → leases → automatic monthly/quarterly billing, custom fees (water/electricity/property/internet), partial payments with auto-remaining, and one-click Chinese bill image generation (with payment QR code) to forward to tenants via WeChat.

## 功能 Features

- 多用户登录（PBKDF2 密码哈希，首建管理员 `admin / admin123`）
- 物业 / 房间 / 租客 / 租约管理（起止日期、押金、付租日、月付/季付）
- 按月自动出账（幂等补账），逾期 / 到期标识
- 账单杂费自定义、部分收款、状态自动迁移（未付 / 部分已付 / 已结清）
- 账单图片导出（SkiaSharp 中文渲染 + 收款二维码），手机保存后微信转发
- 手机优先的自适应界面

## 技术栈 Tech Stack

- ASP.NET Core **Blazor Server**（.NET 10 LTS）
- **EF Core 10 + PostgreSQL 17**（Npgsql）
- **SkiaSharp** 账单图片渲染（MIT 许可）
- xunit 单元测试（19 个用例）
- Docker 多架构镜像（amd64 / arm64），推送 GHCR

## 部署 Deployment

### 方式一：Docker Compose（推荐）

最简单，一条命令同时启动数据库与应用：

```bash
# 可选：改数据库密码与端口（默认值即可直接用）
# export DB_PASSWORD=your-strong-password
# export WEB_PORT=8080

docker compose up -d
```

打开 `http://localhost:8080`（NAS 上换成 `http://NAS地址:8080`），默认账号 `admin / admin123`（首次登录请改密）。

升级到新版本：`docker compose pull && docker compose up -d`。

### 方式二：Docker 原生命令

不想用 compose 时，分两步手动启动：

```bash
# 1) 创建专用网络（让两个容器互相解析）
docker network create propertyledger-net

# 2) 启动 PostgreSQL 数据库
docker run -d --name propertyledger-db \
  --restart unless-stopped \
  --network propertyledger-net --network-alias db \
  -e POSTGRES_USER=property \
  -e POSTGRES_PASSWORD=property \
  -e POSTGRES_DB=propertyledger \
  -e TZ=Asia/Shanghai \
  -v propertyledger-pgdata:/var/lib/postgresql/data \
  postgres:17-alpine

# 3) 启动应用（等待约 10 秒数据库就绪后执行）
docker run -d --name propertyledger-web \
  --restart unless-stopped \
  --network propertyledger-net \
  -p 8080:8080 \
  -e ConnectionStrings__Default="Host=db;Port=5432;Database=propertyledger;Username=property;Password=property" \
  -e TZ=Asia/Shanghai \
  ghcr.io/szboboxing/property-ledger:latest
```

升级：`docker pull ghcr.io/szboboxing/property-ledger:latest && docker rm -f propertyledger-web` 后重新执行第 3 步（数据在 `propertyledger-pgdata` 卷中，不受影响）。

### 数据备份与恢复

数据全部保存在 PostgreSQL 数据卷中，与容器无关：

```bash
# 备份
docker exec propertyledger-db pg_dump -U property propertyledger > backup-$(date +%Y%m%d).sql

# 恢复
cat backup-20261009.sql | docker exec -i propertyledger-db psql -U property propertyledger
```

## 飞牛 NAS（fnOS）部署

1. 在 NAS 上创建目录，放入本仓库的 `docker-compose.yml`（SSH 上传，或在 fnOS 文件管理器中新建）
2. SSH 进入该目录执行 `docker compose up -d`，也可在 fnOS 的 Docker 管理界面「Compose」中导入该文件
3. 浏览器访问 `http://NAS地址:8080`
4. 上传收款码：登录后进入「设置」页粘贴微信/支付宝收款码图片

## 本地开发

```bash
# 需要 .NET 10 SDK；数据库可用本地 PostgreSQL 或：
docker run -d --name propertyledger-db -p 5432:5432 \
  -e POSTGRES_USER=property -e POSTGRES_PASSWORD=property \
  -e POSTGRES_DB=propertyledger postgres:17-alpine

dotnet run --project src/PropertyLedger.Web   # http://localhost:5120
dotnet test                                    # 运行单元测试
```

连接串通过环境变量 `ConnectionStrings__Default` 覆盖（见 `appsettings.json`）。

## 镜像 GHCR

```
ghcr.io/szboboxing/property-ledger:latest
```

推送 `v*.*` 标签触发 GitHub Actions：先跑测试，再多架构构建并推送 GHCR。

## License

[MIT](LICENSE)
