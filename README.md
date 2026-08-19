# CatClawMusic.Plugins.Lrclib

猫爪音乐（CatClawMusic）的**在线歌词插件**，独立于宿主应用编译与交付。

数据源：[LRCLIB](https://lrclib.net) 开放歌词库（免费、无需 API Key、面向开源生态），
按 歌名/艺人/专辑/时长 在线匹配同步歌词，为本地及远程歌曲补齐歌词。

## 形态

- 实现宿主 `ILyricsProviderPlugin` 接口（宿主 `LyricsService` 的歌词兜底链：
  同名 `.lrc`、内嵌歌词都找不到时才调用本插件）；
- 实现宿主 `IViewContributorPlugin` 接口：向发现页贡献「歌词匹配」入口页，
  可手动搜索并指定某首歌使用哪份歌词（v1.1.0+）；
- 本工程编译产出独立 DLL，Release 构建后自动复制为
  `CatClawMusic.Plugins.Lrclib.ccp`，在宿主应用的
  **插件管理 → ＋ 添加 → 本地/网络安装**中导入并启用后即生效，零宿主改动。

## 功能

**自动匹配**（`ILyricsProviderPlugin`，优先级从高到低）

1. **手动覆盖记录**：用户在「歌词匹配」页指定的 LRCLIB 曲目，按 歌名|艺人
   持久化到 `{LocalApplicationData}/CatClawMusic.Maui/lrclib_overrides.json`，
   不联网秒返回，增删立即生效（不落内存缓存）；
2. **精确匹配**：LRCLIB `/get`（歌名+艺人+专辑+时长）；
3. **搜索兜底**：LRCLIB `/search` 候选评分择优——歌名完全一致加权 +
   时长相近加权 + 同步歌词优先；
4. **防误配**：纯器乐（instrumental）记录直接跳过；已知时长且最佳候选差异
   超过 15 秒且歌名不一致时拒绝返回；
5. **时长单位防御**：宿主 `Song.Duration` 存在秒/毫秒单位不一致的历史问题，
   沿用宿主播放页的防御判断（>1000 视为毫秒）自动归一化；
6. **同步歌词优先**，纯文本歌词兜底（无时间轴整行显示）；
7. **内存缓存**（300 条）：仅缓存自动匹配结果，规避 LRCLIB 限流
   （50 次/分钟/IP）。

**手动匹配入口**（`IViewContributorPlugin`，发现页 → 歌词匹配）

- 输入歌名/艺人搜索 LRCLIB 候选，列表展示 曲目-艺人 / 专辑 · 时长 /
  歌词形态徽标（同步歌词/纯文本/无歌词）；
- 点「使用此歌词」保存为覆盖记录——之后播放该歌自动优先使用这份歌词，
  不再受自动评分误配影响；
- 覆盖记录列表可随时删除，恢复自动匹配；
- 注意：搜索的歌名/艺人需与歌曲标签一致，否则覆盖不会命中。

## 构建

```bash
dotnet build -c Release
```

产物：`bin/Release/net10.0/CatClawMusic.Plugins.Lrclib.ccp`

> 依赖：需要宿主仓库 `CatClawMusic` 中的 `CatClawMusic.Core` 工程（接口与模型定义，
> 本工程以相对路径引用 `..\CatClawMusic\CatClawMusic.Core`）。
> v1.1.0+ 的手动匹配页引用 `Microsoft.Maui.Controls`（10.0.20，与宿主一致）与
> `CommunityToolkit.Mvvm`，均不随插件分发（`CopyLocalLockFileAssemblies=false`，由宿主提供）。

## 协议

[MIT](LICENSE)
