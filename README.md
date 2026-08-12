# CatClawMusic.Plugins.Lrclib

猫爪音乐（CatClawMusic）的**在线歌词插件**，独立于宿主应用编译与交付。

数据源：[LRCLIB](https://lrclib.net) 开放歌词库（免费、无需 API Key、面向开源生态），
按 歌名/艺人/专辑/时长 在线匹配同步歌词，为本地及远程歌曲补齐歌词。

## 形态

- 实现宿主 `ILyricsProviderPlugin` 接口（宿主 `LyricsService` 的歌词兜底链：
  同名 `.lrc`、内嵌歌词都找不到时才调用本插件）；
- 本工程编译产出独立 DLL，Release 构建后自动复制为
  `CatClawMusic.Plugins.Lrclib.ccp`，在宿主应用的
  **插件管理 → ＋ 添加 → 本地/网络安装**中导入并启用后即生效，零宿主改动。

## 功能

- **两级匹配**：先走 LRCLIB `/get` 精确匹配（歌名+艺人+专辑+时长），失败再走
  `/search` 候选评分兜底；
- **评分择优**：歌名完全一致加权 + 时长相近加权 + 同步歌词优先；
- **防误配**：纯器乐（instrumental）记录直接跳过；已知时长且最佳候选差异
  超过 15 秒且歌名不一致时拒绝返回；
- **时长单位防御**：宿主 `Song.Duration` 存在秒/毫秒单位不一致的历史问题，
  沿用宿主播放页的防御判断（>1000 视为毫秒）自动归一化；
- **同步歌词优先**，纯文本歌词兜底（无时间轴整行显示）；
- **内存缓存**（300 条）：重复播放/换页命中缓存，规避 LRCLIB 限流
  （50 次/分钟/IP）。

## 构建

```bash
dotnet build -c Release
```

产物：`bin/Release/net10.0/CatClawMusic.Plugins.Lrclib.ccp`

> 依赖：需要宿主仓库 `CatClawMusic` 中的 `CatClawMusic.Core` 工程（接口与模型定义，
> 本工程以相对路径引用 `..\CatClawMusic\CatClawMusic.Core`）。
> 插件不引用 MAUI，保持纯 .NET 最小依赖面（`CopyLocalLockFileAssemblies=false`）。

## 协议

[MIT](LICENSE)
