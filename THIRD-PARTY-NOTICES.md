# 第三方组件声明

[TaskMonitor](https://github.com/linesoft2/TaskMonitor) 的版权和 Apache-2.0 许可见 [LICENSE](LICENSE)。[更新日志](CHANGELOG.md) 保留了上游记录，并列出 DeskMonitor 的主要变更。

## 宿主

| 组件 | 许可与来源 |
| --- | --- |
| DirectN 1.18.0 | [MIT](licenses/DirectN.txt) · [源码](https://github.com/smourier/DirectN) |
| FluentWpfCore 1.0.5 | [MIT](licenses/FluentWpfCore.txt) · [源码](https://github.com/TwilightLemon/FluentWpfCore/tree/39b2b22a8375a154baa72a83b91f9d1d8a35f5e7) |
| iNKORE.UI.WPF 1.2.8 | [LGPL-2.1-or-later](licenses/iNKORE.UI.WPF.txt) · [源码](https://github.com/iNKORE-NET/UI.WPF/tree/v1.2.8) |
| iNKORE.UI.WPF.Modern 0.10.2.1（含 Controls 程序集） | [上游许可](licenses/iNKORE.UI.WPF.Modern.txt) · [源码](https://github.com/iNKORE-NET/UI.WPF.Modern) · [NuGet 包](https://www.nuget.org/packages/iNKORE.UI.WPF.Modern/0.10.2.1) |
| YamlDotNet 16.3.0 | [MIT](licenses/YamlDotNet.txt) · [源码](https://github.com/aaubry/YamlDotNet/tree/ae480660f4fb26f3eb0b41c1d1fcf21c0e9d9e73) |
| Fody 6.8.0、Costura.Fody 5.7.0 | [Fody 许可](licenses/Fody.txt) · [Costura 许可](licenses/Costura.Fody.txt) · [Costura 源码](https://github.com/Fody/Costura) |
| .NET 兼容性组件 | [Microsoft 许可](licenses/Microsoft.NET.txt) · [第三方声明](licenses/Microsoft.NET.ThirdParty.txt) |

本仓库提供对应版本的宿主源码。使用 `dotnet build DeskMonitor.csproj -c Release -p:DisableFody=true` 可构建使用外置 DLL 的版本，以便替换依赖后调试和重新构建。直接构建时使用的准确依赖版本记录在 [DeskMonitor.csproj](DeskMonitor.csproj) 中。

## 外设电量插件

插件包内包含 Wireless Battery 1.0.8 与 HeadsetControl 4.3.0 的分发和许可材料，以及对应源码。参见[第三方归档文件、校验值与提取说明](extensions/peripheral-battery/vendor/README.md)。
