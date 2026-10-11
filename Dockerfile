# ── 构建阶段 ──
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY PropertyLedger.slnx ./
COPY src/PropertyLedger.Core/PropertyLedger.Core.csproj src/PropertyLedger.Core/
COPY src/PropertyLedger.Web/PropertyLedger.Web.csproj src/PropertyLedger.Web/
RUN dotnet restore src/PropertyLedger.Web/PropertyLedger.Web.csproj
COPY src/ src/
# 注意：publish 不能加 --no-restore，否则 Linux 下不会产出 wwwroot/_framework/blazor.web.js，容器内交互全挂
# 指定 linux musl RID：不带则把全平台（win/osx/arm 等）native 库都打进 runtimes/，白白多出约 450MB
ARG TARGETARCH
RUN RID="linux-musl-x64"; if [ "$TARGETARCH" = "arm64" ]; then RID="linux-musl-arm64"; fi; \
    dotnet publish src/PropertyLedger.Web/PropertyLedger.Web.csproj -c Release -r $RID --self-contained false \
        -p:DebugType=none -p:DebugSymbols=false -o /app/publish \
    && rm -f /app/publish/web.config

# ── 运行阶段 ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# SkiaSharp 账单图片渲染需要 fontconfig（含 freetype）；tzdata 供 TZ 时区生效
RUN apk add --no-cache fontconfig tzdata \
    && fc-cache -f

# 内置单一中文字体（Noto Sans SC Regular，约 8MB；替代全量 fonts-noto-cjk 的 ~100MB），粗体由 SkiaSharp 合成加粗
COPY fonts/NotoSansSC-Regular.otf /app/fonts/NotoSansSC-Regular.otf
COPY --from=build /app/publish ./

# 数据卷：数据库连接串由 docker-compose 注入；此处只声明时区与端口
ENV TZ=Asia/Shanghai \
    ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "PropertyLedger.Web.dll"]
