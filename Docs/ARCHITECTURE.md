# 架构说明

本文件只描述公开可核对的运行时结构；内部测试证据、冻结过程与判定细节不随仓库分发。

## 目录与加载

```text
TV_BiliBili/                  # 仓库根目录即 mod 文件夹
├── manifest.xml              # ID、名称、作者、版本、能力、程序集
├── TV_BiliBili.dll           # IMod 入口与实现
├── Docs/                     # 用户文档
└── Source/                   # C# 源码与工程文件
```

《深空当铺》的 ModLoader 从 `Mods/<mod 文件夹>/` 根目录读取 `manifest.xml` 和程序集；本项目不使用 RimWorld 式 `Assemblies/` 子目录。

## 运行时链路

1. 玩家在日历第 4 标签输入 B 站网页链接或 BV 号。
2. 入口类用正则提取 BV 号，调用 B 站 `view` 接口取 cid。
3. 用 cid 调 `playurl` 接口取合流直链；非 http(s) 直链被拒绝。
4. `VideoPlayer` 播放直链，画面写入 `RenderTexture`。
5. 同一 `RenderTexture` 显示在标签页 `RawImage`，并四角贴合投到店铺日历显示器。
6. 生命周期事件维护暂停 / 续播 / 释放；ESC 与选项菜单通过 Harmony 补丁获得开闭通知。

## 能力与安全

- manifest 唯一能力：`network`。
- 无 `files` / `process` / `native` 能力；无前置 mod。
- 不下载视频、不缓存视频、不写玩家存档；只在自身 ModData 写纯文本运行日志。
- 12 个运行时引用均由游戏提供，不随 mod 分发；无 NuGet 依赖。
