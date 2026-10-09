# ── 构建阶段 ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY PropertyLedger.slnx ./
COPY src/PropertyLedger.Core/PropertyLedger.Core.csproj src/PropertyLedger.Core/
COPY src/PropertyLedger.Web/PropertyLedger.Web.csproj src/PropertyLedger.Web/
RUN dotnet restore src/PropertyLedger.Web/PropertyLedger.Web.csproj
COPY src/ src/
# 注意：publish 不能加 --no-restore，否则 Linux 下不会产出 wwwroot/_framework/blazor.web.js，容器内交互全挂
RUN dotnet publish src/PropertyLedger.Web/PropertyLedger.Web.csproj -c Release -o /app/publish

# ── 运行阶段 ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# SkiaSharp 账单图片渲染需要：中文字体 + fontconfig
RUN apt-get update \
    && apt-get install -y --no-install-recommends fonts-noto-cjk libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

# 数据卷：数据库连接串由 docker-compose 注入；此处只声明时区与端口
ENV TZ=Asia/Shanghai \
    ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "PropertyLedger.Web.dll"]
