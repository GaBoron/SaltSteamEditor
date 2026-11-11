# Salt Steam 状态编辑器

[SaltSteamPlugin](https://github.com/GaodaGG/SaltSteamPlugin) 的独立 Windows 配套工具，可视化编辑 Steam 丰富状态的 `songFormat` 模板。

- 点选歌曲名、歌手、专辑、歌词和时间字段，调整片段顺序，编辑固定文字。
- 内置常用模板，支持撤销、重做和直接编辑模板文本。
- 使用可修改的示例数据即时预览，检查缺失信息、未知字段和过长内容。
- 采用 Fluent 界面与图标，支持深色、浅色主题切换。
- 保存时只修改 `songFormat`，保留缓存与其他配置；替换原文件前留下 `.bak` 备份。

## 使用

需要 Windows 10/11 x64，并已安装、启用原插件。便携包自带运行时，解压后运行 `SaltSteamEditor.exe`。

1. 在 SPW 的数据目录中找到 **SaltSteamPlugin 实际使用的 `config.json`**，点击“打开配置”或将文件拖入窗口。应包含字符串字段 `songFormat`；不要选择定义设置页面的 `preference_config.json`。不同安装方式的数据位置可能不同，可在 SPW 中查看数据目录。
2. 应用预设或点选字段。选择片段后可左移、右移、移除，固定文字可单独编辑；也能直接输入模板文本。
3. 点击“保存到配置”。原文件备份在同一目录，名称为 `config.json.时间-标识.bak`。如果其他程序改过模板，工具会阻止覆盖；重新打开配置后再编辑。
4. 回到 SPW 检查状态。原插件监听配置变化；如未更新，可切歌或重新启用插件。

没有现成配置时，可先设计，再“另存为”一个只含 `songFormat` 的 `config.json`；需放入原插件实际使用的位置才能生效。另存到已有配置会保留该文件的其他字段并备份；不覆盖无法识别的 JSON 文件。

预览是本地示意，不读取正在播放的音乐，也不向 Steam 发送状态。原插件控制状态刷新频率，Steam 展示还可能带有本地化前缀。较长的实际歌名或歌词可能超过 Steam 上限；示例长度检查不代表所有歌曲都能显示。模板中的换行及控制字符需移除后才能保存。

快捷键：`Ctrl+O` 打开、`Ctrl+S` 保存、`Ctrl+Shift+S` 另存为；编辑模板时 `Ctrl+Z` 撤销、`Ctrl+Y` 重做。恢复备份时先关闭 SPW，将当前配置移开，再把需要的备份复制为 `config.json`。

## 构建

安装 .NET 10 SDK，在仓库根目录执行：

```powershell
dotnet build src/SaltSteamEditor/SaltSteamEditor.csproj -c Release
./Publish-Portable.ps1
```

模板字段和替换顺序依据原插件的 [Config.java](https://github.com/GaodaGG/SaltSteamPlugin/blob/89232ab/src/main/java/com/gg/SaltSteamPlugin/Config.java)，兼容该提交的配置结构。Steam 长度限制参考 [ISteamFriends 文档](https://partner.steamgames.com/doc/api/ISteamFriends#SetRichPresence)。原插件及其许可不随本工具一同打包。

便携包生成在 `publish` 目录，包含运行时与所需的第三方声明；输出目录需为空。可通过 `-OutputDirectory` 指定其他目录。

界面使用 [WPF UI](https://github.com/lepoco/wpfui)，布局与文字层级参考 [Microsoft Fluent 2](https://fluent2.microsoft.design/typography)。第三方声明见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
