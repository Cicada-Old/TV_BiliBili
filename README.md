# 店铺小电视：也许是B站？

> 给《深空当铺：可能是偷的》的店铺日历加一个 B 站播放器标签页。  
> [English](README.en.md) · 《深空当铺：可能是偷的》1.0.0 · 作者 Cicada · MIT

## 这是什么

在店铺日历中新增第 4 个标签页「播放器」。粘贴 B 站视频网页链接或纯 BV 号即可播放，画面同时四角贴合投到店铺日历显示器；点显示器仍打开原版日历。

## 安装

1. 关闭游戏。
2. 下载 `v1.0.0` Release 的 `TV_BiliBili_1.0.0.zip` 并解压，得到 `TV_BiliBili` 文件夹。
3. 将**整个 `TV_BiliBili` 文件夹**放进游戏根目录的 `Mods/`（与 `Probably Stolen.exe` 同级）。
4. 启动游戏，在 Mod 列表启用本 Mod。
5. 首次启用时，在游戏内批准 `network`（联网）能力；不批准则无法取得视频流。

也可以直接下载本仓库源码 ZIP：仓库根目录就是 mod 文件夹，把解压后的文件夹复制为 `Mods/TV_BiliBili/` 即可。

mod 文件夹里必需的运行文件只有 `TV_BiliBili.dll` + `manifest.xml`；Release 压缩包另附用户文档，文档不参与加载。不要额外复制第三方 DLL。

## 文档

| 想看什么 | 去哪 |
|---|---|
| 安装、使用、卸载 | [安装与使用](Docs/安装与使用.md) |
| 兼容游戏版本、依赖、能力声明 | [兼容性与依赖](Docs/兼容性与依赖.md) |
| 功能说明 | [FEATURES](Docs/FEATURES.md) / [English](Docs/FEATURES.en.md) |
| 已知问题 | [KNOWN_ISSUES](Docs/KNOWN_ISSUES.md) / [English](Docs/KNOWN_ISSUES.en.md) |
| 测试结论 | [TESTING](Docs/TESTING.md) / [English](Docs/TESTING.en.md) |
| 架构说明 | [ARCHITECTURE](Docs/ARCHITECTURE.md) / [English](Docs/ARCHITECTURE.en.md) |
| 贡献指南 | [CONTRIBUTING](Docs/CONTRIBUTING.md) / [English](Docs/CONTRIBUTING.en.md) |
| 版本变化 | [更新说明](Docs/更新说明.md) / [CHANGELOG](CHANGELOG.md) / [English](CHANGELOG.en.md) |
| 自己构建 | [开发说明](DEVELOPMENT.md) / [English](DEVELOPMENT.en.md) |
| 版本指纹与归档摘要 | [ARCHIVE](ARCHIVE.md) |

## 仓库结构

仓库根目录就是 mod 文件夹；`manifest.xml` 与 `TV_BiliBili.dll` 必须留在根目录，这是游戏加载器的要求。

```text
TV_BiliBili/
├── manifest.xml          # mod 身份、版本、能力
├── TV_BiliBili.dll       # 运行程序集
├── Docs/                 # 用户文档
├── Source/               # C# 源码与 csproj
├── README.md / README.en.md
├── CHANGELOG.md / CHANGELOG.en.md
├── DEVELOPMENT.md / DEVELOPMENT.en.md
├── ARCHIVE.md
├── LICENSE
├── .gitignore
└── .gitattributes
```

## 发布包

`v1.0.0` Release 提供 `TV_BiliBili_1.0.0.zip`：顶层为 `TV_BiliBili/`，内含最小运行包 `TV_BiliBili.dll` + `manifest.xml` 及 `Docs/` 用户文档。源码与内部资料不从 Release 分发；内部开发证据、测试记录和技术归档不在本仓库。
