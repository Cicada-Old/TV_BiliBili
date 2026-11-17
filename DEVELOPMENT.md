# 开发说明

> 本文件只写「怎么构建」。功能、兼容、已知问题见 `Docs/`，版本摘要见 `ARCHIVE.md`。

## 1. 源码位置

```text
Source/
├── TvBiliBiliMod.cs
└── TV_BiliBili.csproj
```

仓库根目录是 mod 文件夹：`manifest.xml` 与 `TV_BiliBili.dll` 必须留在根目录，这是《深空当铺》加载器的要求，不要移到 `Assemblies/` 之类的子目录。

## 2. 构建

```
dotnet build Source/TV_BiliBili.csproj -c Release
```

- 目标框架：`netstandard2.1`
- 实测 SDK：.NET 10.0.301
- 输出：`Source/bin/Release/netstandard2.1/TV_BiliBili.dll`
- 发布时把输出 DLL 复制到仓库根目录，与 `manifest.xml` 组成 mod 文件夹。

## 3. 引用程序集

`Source/TV_BiliBili.csproj` 保留了发布冻结时的 12 个 `HintPath`，这些路径指向构建机上的游戏目录：

```text
<游戏安装目录>/Probably Stolen_Data/Managed/
```

在其他机器上构建时，把这 12 条 `HintPath` 改到你自己的游戏目录即可。全部引用都是只读编译引用（`<Private>false</Private>`），不要复制到发布包；没有 NuGet 包。

## 4. 验证

- 常规开发：确认构建 0 错误、进游戏能加载并在 Mod 列表启用即可。
- 归档核对：若使用与冻结时相同的引用程序集与编译顺序，重建 DLL 的 md5 应为 `b0e773a7597814123b7fa633ad1b6100`。不一致时先查引用程序集来源与编译选项，不要为凑 md5 改代码。

## 5. 目录约定

| 路径 | 内容 |
|---|---|
| `Source/` | C# 源码与 csproj |
| `Docs/` | 用户可见文档 |
| 根目录 | `manifest.xml`、`TV_BiliBili.dll`、README、CHANGELOG、ARCHIVE、DEVELOPMENT、LICENSE |
| `bin/`、`obj/` | 构建产物，已由 `.gitignore` 排除 |
