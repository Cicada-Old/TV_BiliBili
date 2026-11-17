# ARCHIVE — 店铺小电视：也许是B站？ 1.0.0

> 公开的归档摘要。对应的内部测试报告、证据与冻结过程不随仓库分发。

## 1. 项目与标识

| 项 | 值 |
|---|---|
| 项目名 | 店铺小电视：也许是B站？ / TV_BiliBili |
| Mod ID | `TV_BiliBili` |
| 作者 | `Cicada` |
| 发布版本 | `1.0.0` |
| 仓库 | `Cicada-Old/TV_BiliBili` |
| 许可证 | MIT，`Copyright (c) 2026 Cicada` |

## 2. 支持的游戏版本

《深空当铺：可能是偷的》1.0.0（覆盖该版本全部修订版）。精确 Rev 未钉版。

## 3. 运行与构建环境

| 项 | 值 |
|---|---|
| 引擎 / 后端 | Unity 2021.3.45f2 / Mono |
| 目标框架 | `netstandard2.1` |
| SDK | .NET 10.0.301（构建时实测） |
| 构建命令 | `dotnet build Source/TV_BiliBili.csproj -c Release` |
| 运行文件位置 | 仓库根目录：`manifest.xml` + `TV_BiliBili.dll` |

## 4. 源代码与产物位置

| 项 | 路径 | md5 |
|---|---|---|
| 源码 | `Source/TvBiliBiliMod.cs` | `31f1c464147f53cedcbbfe13e3f961ce` |
| 工程文件 | `Source/TV_BiliBili.csproj` | `70ddfdee979bbbb91495f632d044d21f` |
| 程序集 | `TV_BiliBili.dll` | `b0e773a7597814123b7fa633ad1b6100` |
| 清单 | `manifest.xml` | `2857b35e83f9923add910dcc887d176b` |

发布文件包与源码构建产物的同一性校验见 Release 页与 `Docs/兼容性与依赖.md`。内部冻结锚点、测试判定表与技术归档不在此公开。

## 5. 第三方依赖

12 个运行时引用全部由游戏提供，`Private=false`，无 NuGet 包、无随包第三方 DLL：

`Assembly-CSharp`、`UnityEngine`、`UnityEngine.CoreModule`、`UnityEngine.VideoModule`、`UnityEngine.UnityWebRequestModule`、`UnityEngine.AudioModule`、`UnityEngine.UI`、`UnityEngine.UIModule`、`UnityEngine.TextRenderingModule`、`Unity.TextMeshPro`、`Newtonsoft.Json`、`0Harmony`。

manifest 唯一能力声明为 `network`；无前置 mod。

## 6. 公开发布边界

Release 压缩包只含 mod 运行文件与用户文档；不分发内部证据、测试记录、技术归档、存档或游戏本体文件。安装与卸载说明见 [安装与使用](Docs/安装与使用.md)。
