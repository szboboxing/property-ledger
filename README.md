# 物业管理台账 Property Ledger

房东记账工具：物业 → 房间 → 租客 → 租约 → 按月/季自动出账，支持自定义杂费（水电/物业/网费）、部分收款自动算剩余，一键生成中文账单图片（含收款二维码）用微信发给租客。

A landlord's ledger: properties → rooms → tenants → leases → automatic monthly/quarterly billing, custom fees (water/electricity/property/internet), partial payments with auto-remaining, and one-click Chinese bill image generation (with payment QR code) to forward to tenants via WeChat.

## 功能 Features

- 登录即首页：打开站点直接呈现账号密码登录页，登录后进入仪表盘
- 双角色：**总管理员**（全部功能 + 用户管理）/ **普通用户**（按分配的功能显示界面）
- 总管理员可管理用户账号：创建用户、重置密码、停用/启用、按用户勾选可用功能（账单/报表/租约/房间/租客/物业/设置）
- 物业 / 房间 / 租客 / 租约管理（起止日期、押金、付租日、月付/季付）；物业支持类型（房屋/商铺/公寓）与管理处、水电燃气联系方式
- 按月自动出账（幂等补账），逾期 / 到期标识
- 补录过往账单：接手旧账/历史欠款可按租约 + 月份范围补录（支持连续多月，已退租租约也可补），金额留空自动按租约标准计算；补录记录带「补录」标识、录入时间与「只看补录」筛选
- 报表：应收 / 已收 / 未收 / 收款率总览，按账期月份（按年份分页，每页 12 个自然月）与按物业分组统计（含合计行），补录账单独行小计，支持按年份筛选
- 账单杂费自定义、部分收款、状态自动迁移（未付 / 部分已付 / 已结清）
- 富文本备注：物业 / 房间 / 租客 / 租约 / 账单备注均支持加粗、斜体、下划线与**上传图片**（登录可见，文件落库外存储并随数据卷持久化）
- 账单图片导出（SkiaSharp 中文渲染 + 收款二维码），手机保存后微信转发
- 桌面常驻侧边栏（彩色图标导航），手机端抽屉菜单，移动端自适应
- 多用户登录（PBKDF2 密码哈希，首建总管理员 `admin / admin123`）

## 技术栈 Tech Stack

- ASP.NET Core **Blazor Server**（.NET 10 LTS）
- **EF Core 10 + PostgreSQL 17**（Npgsql）
- **SkiaSharp** 账单图片渲染（MIT 许可）
- xunit 单元测试（44 个用例）
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
  -v propertyledger-uploads:/app/uploads \
  -e ConnectionStrings__Default="Host=db;Port=5432;Database=propertyledger;Username=property;Password=property" \
  -e TZ=Asia/Shanghai \
  ghcr.io/szboboxing/property-ledger:latest
```

升级：`docker pull ghcr.io/szboboxing/property-ledger:latest && docker rm -f propertyledger-web` 后重新执行第 3 步（数据在 `propertyledger-pgdata`、备注图片在 `propertyledger-uploads` 卷中，不受影响）。

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

## 更新日志 Changelog

### v1.5（2026-10-10）
- 补录记录清晰化：账单筛选栏新增**「只看补录」**开关（补录成功后自动勾选，直接查看新补账单）；账单详情面板显示**「补录账单 · 录入于 …」**录入时间
- 报表体现补录：状态计数下方新增**补录账单小计行**（张数 + 补录的应收 / 已收 / 未收），并注明已计入总合计（跟随年份筛选）
- 报表按账期月份改为**按年份划块**：每个年份独立成卡片块（醒目标题条 + 12 行自然月 + 该年合计），选「全部年份」时所有年份块倒序展示，选某年只显示该年块
- 报表按物业分组新增**合计行**（账单数 / 应收 / 已收 / 未收 / 收款率）

### v1.4（2026-10-10）

- 补录过往账单支持**连续多月**：起止月份范围一次补录（单月 = 起止相同），范围内已有账单的月份自动跳过、租期外的月份忽略，完成后汇总提示「成功 X 张 / 跳过 Y / 租期外 Z」
- 设置页新增**清空业务数据**（仅总管理员可见）：一键清空全部物业、房间、租客、租约、账单、收款记录及备注图片，保留登录账号与基础设置（房东称呼、收款提示语、收款码）；需输入「清空」两字二次确认，适用于台账重置给新一批物业
- 单元测试 39 → 44（多月补录边界 / 清空数据保留校验）

### v1.3（2026-10-10）

- 物业增强：新增类型选择（房屋 / 商铺 / 公寓），新增「管理处联系方式」「水电燃气联系方式」两列
- 富文本备注：物业 / 房间 / 租客 / 租约 / 账单备注全部升级为富文本编辑器（加粗 / 斜体 / 下划线 / 清除格式），支持**上传图片**（jpg/png/gif/webp，单张 ≤ 5MB，登录可见；列表中以摘要 +「查看」展开）
- 补录过往账单：账单页新增补录入口，选择租约 + 账期月份即可补录历史欠款（已退租租约也可补录）；金额留空自动按租约标准计算（月付 = 月租，季付 = 三个月租金），补录账单带「补录」标识
- 新增报表页（普通用户可按需授权）：应收 / 已收 / 未收 / 收款率汇总卡 + 状态计数，按账期月份、按物业两组明细，支持按年份筛选
- 备注图片持久化：Docker 部署新增 `uploads` 数据卷
- 单元测试扩充至 39 个（补录校验 / HTML 摘要 / 报表权限）

### v1.2（2026-10-09）

- 修复：容器部署下 `blazor.web.js` 404 导致页面无交互（添加/保存按钮全部无反应）——发布阶段补上 `MapStaticAssets()`，Dockerfile 去掉 publish `--no-restore`
- 修复：HTML 响应增加 `Cache-Control: no-cache`，应用升级后浏览器不再引用被替换的旧资源
- 更名：应用标题由"物业管理小工具"改为"物业管理台账"（登录页/侧边栏/顶栏/页面标题）
- 兼容性加固：侧边栏定位改用全兼容写法，覆盖更多浏览器环境

### v1.1（2026-10-09）

- 首页调整为登录界面：未登录打开站点直接显示登录页，登录后进入仪表盘
- 用户角色细分：总管理员 / 普通用户，界面与功能随角色区分
- 用户管理增强：按用户勾选可用功能（账单记账/租约/房间/租客/物业/设置），总管理员可重置密码、停用启用
- 导航重构：桌面常驻侧边栏（彩色图标），手机端抽屉菜单；物业/房间/租客/租约等各功能独立页面入口清晰可见
- 安全加固：所有业务页面补齐登录与功能级授权（此前未登录可直接访问页面）
- 新增 6 个权限单元测试（共 25 个）

### v1.0（2026-10-09）

- 首个版本：物业/房间/租客/租约、自动出账、杂费与部分收款、账单图片、Docker 多架构部署

## License

[MIT](LICENSE)
