# v2.1 发布说明

## 镜像大幅精简

### 📦 565MB → 244MB（省约 57%）
- **基础镜像换 alpine**：aspnet:10.0 → aspnet:10.0-alpine，运行时更小更轻
- **中文字库瘦身**：全量 fonts-noto-cjk（101MB，简繁日韩全带）→ 内置 Noto Sans SC 单字体（8MB），粗体由 SkiaSharp 合成加粗
- 账单图渲染效果不变（中文字形、标题、脱敏均已实测），双架构（amd64 / arm64）不变

### 🚀 部署更轻松
- 镜像拉取 / 应用内一键升级更快，弱网环境与离线部署（docker load）传输量更小

## 升级说明
- v1.9+ 部署可在应用内直接一键升级（设置 → 软件更新）；或手动 `docker compose pull web && docker compose up -d web`
- 本版应用功能无变化、无数据库结构变更，升级无数据迁移

## 测试
- 单元测试 86 个全部通过
