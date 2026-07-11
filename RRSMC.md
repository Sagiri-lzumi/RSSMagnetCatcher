# RSS Magnet Collector：轻量化 Windows 托盘应用项目架构文档

> 方向：个人自用、轻量化、便携式、剪贴板导出、条件筛选、托盘常驻  

---

## 1. 项目定位

本项目是一个运行在 Windows 上的轻量级后台托盘工具，用于：

1. 管理多个 RSS/Atom 订阅地址；
2. 按设定间隔自动检查更新；
3. 从 RSS 条目中提取 magnet 链接；
4. 根据条件、正则表达式、清晰度、语言等规则自动筛选；
5. 将符合条件的 magnet 链接复制到剪贴板；
6. 支持全部复制、勾选复制、按订阅复制、按当前筛选条件复制；
7. 用托盘图标显示当前运行状态、网络状态和 RSS 检查状态。

项目不追求复杂数据库、账号系统、远程同步和下载器集成，优先保证：

```text
轻量、简单、便携、后台稳定、复制方便、状态清晰。
```

---

## 2. 推荐技术方案

### 2.1 主推荐方案

```text
语言：C#
运行时：.NET 8 LTS 或更新的 .NET LTS
界面：WinForms
托盘：NotifyIcon
网络：HttpClient
RSS/XML 解析：XDocument / XmlReader
规则匹配：Regex
本地存储：JSON + JSONL 文件
导出方式：Clipboard 剪贴板
日志：文本日志文件
```

### 2.2 为什么不用数据库

个人自用版暂时不需要任何数据库。直接用文件即可：
也就是直接保存到本项目的目录内就行，使用绝对地址，项目在哪就保存在项目的哪个位置，比如本项目下的data文件夹内

```text
配置少，用 JSON 足够；
历史条目只是追加记录，用 JSONL 足够；
不需要复杂查询；
备份和迁移更方便；
整个程序文件夹可以直接复制到另一台电脑。
```

---

## 3. 运行方式

### 3.1 程序形态

```text
RRSMC.exe
```

双击运行后进入系统托盘。关闭主窗口时不退出程序，而是最小化到托盘。

### 3.2 是否开机自启

设置中提供：

```text
[ ] 开机自动启动
[ ] 启动后直接进入托盘
[ ] 启动后立即检查全部订阅
```

开机自启可以通过写入当前用户启动项实现，不需要安装成 Windows Service。

---

## 4. 项目文件夹结构

```text
RSSMagnetCatcher/
├─ RRSMC.exe
├─ data/
│  ├─ app.settings.json          # 全局设置
│  ├─ feeds.json                 # RSS 订阅列表
│  ├─ rules.json                 # 条件筛选与正则规则
│  ├─ feed_state.json            # 每个 RSS 的最后检查状态
│  ├─ item_cache.jsonl           # 已发现条目缓存
│  ├─ export_history.jsonl       # 已复制/已导出记录
│  └─ logs/
│     ├─ app.log
│     └─ error.log
├─ export/
│  └─ manual/                    # 可选：手动保存导出文本
└─ backup/
   └─ auto/                      # 可选：自动备份配置
```

---

## 5. 核心功能总览

| 模块 | 功能 |
|---|---|
| RSS 管理 | 添加、编辑、删除、启用、禁用、分组订阅 |
| 定时检查 | 全局检查间隔、单订阅检查间隔、手动立即检查 |
| 网络诊断 | 判断本机网络、DNS、HTTP 状态、RSS 内容是否有效 |
| RSS 解析 | 兼容 RSS 2.0、Atom、自定义字段 |
| magnet 提取 | 从多个字段扫描 magnet 链接 |
| torrent 识别 | 找不到 magnet 时识别 torrent 链接并提示 |
| 条件筛选 | 支持按钮条件、正则表达式、包含/排除规则 |
| 勾选导出 | 条目左侧 checkbox，可复制勾选项 |
| 全部导出 | 一键复制全部未使用或全部当前筛选结果 |
| 剪贴板导出 | 默认复制到剪贴板，一行一个 magnet |
| 托盘状态 | 正常、检查中、有新增、失败、离线、暂停 |
| 提醒 | 新增提醒、失败提醒、汇总提醒 |
| 便携存储 | 所有数据放在程序目录 data 文件夹内 |

---

## 6. 主界面设计

### 6.1 主窗口整体布局

```text
┌────────────────────────────────────────────────────────────────────┐
│ RSSMagnetCatcher                                     _  □  ×   │
├────────────────────────────────────────────────────────────────────┤
│ 状态：正常 | RSS：12 个 | 新增：5 | 已勾选：5 | 下次检查：28 分钟后 │
├────────────────────────────────────────────────────────────────────┤
│ 条件：[ (GBK|CHS|简);(1080p)      ] [GBK|CHS|简] [BIG5|CHT|繁]      │
│      [720p] [1080p] [1080p+] [HEVC] [字幕] [条件选择] [×]          │
├───────────────┬────────────────────────────────────────────────────┤
│ 订阅列表       │ 条目列表                                             │
│               │                                                    │
│ 全部订阅       │ [✓] NEW  标题 A                 1080p 简  已提取     │
│ 未导出         │ [✓] NEW  标题 B                 2160p 简  已提取     │
│ 有新增         │ [ ]      标题 C                 720p  繁  被规则过滤 │
│ 检查失败       │ [ ]      标题 D                 -     -   无 magnet  │
│               │                                                    │
│ 分组：动画     │                                                    │
│ 分组：剧集     │                                                    │
│ 分组：其他     │                                                    │
├───────────────┴────────────────────────────────────────────────────┤
│ [立即检查] [勾选当前筛选] [取消当前勾选] [复制勾选] [复制当前筛选] │
└────────────────────────────────────────────────────────────────────┘
```

---

## 7. 条件栏设计

你给的参考图可以作为条件栏的主要形态：

```text
条件：[ (GBK|CHS|简);(1080p) ]  GBK|CHS|简  BIG5|CHT|繁  720p  1080p  [条件选择] [×]
```

### 7.1 条件栏组成

| 区域 | 说明 |
|---|---|
| `条件：` | 固定标签 |
| 输入框 | 当前条件表达式，可直接手写正则或组合条件 |
| 条件快捷按钮 | 常用条件，一点就追加到输入框 |
| 条件选择按钮 | 打开完整条件选择面板 |
| `×` 按钮 | 清空当前条件 |

### 7.2 条件表达式语法

建议使用简单规则，避免做复杂表达式解析器。

```text
; 表示 AND，并且满足
| 表示 OR，满足其中之一
! 表示 NOT，排除
```

示例：

```text
(GBK|CHS|简);(1080p)
```

含义：

```text
标题或字段中必须包含 GBK、CHS、简 其中之一；
同时必须包含 1080p。
```

示例：

```text
(GBK|CHS|简);(1080p|2160p|4k);!(720p)
```

含义：

```text
简体；
1080p 或更高；
排除 720p。
```

---

## 8. 条件快捷按钮

### 8.1 默认快捷按钮

界面默认提供这些快捷按钮：

```text
GBK|CHS|简
BIG5|CHT|繁
720p
1080p
1080p+
2160p|4K
HEVC|H265
AVC|H264
字幕|内嵌|外挂
```

### 8.2 点击行为

点击快捷按钮时，不直接替换原条件，而是追加到条件输入框。

例如当前为空，点击：

```text
GBK|CHS|简
```

输入框变成：

```text
(GBK|CHS|简)
```

再点击：

```text
1080p
```

输入框变成：

```text
(GBK|CHS|简);(1080p)
```

### 8.3 清空按钮

点击 `×` 后：

```text
清空条件输入框；
条目列表恢复显示全部；
不会删除已保存规则。
```

---

## 9. 条件选择面板

点击 `条件选择` 后弹出面板。

```text
┌──────────────────────── 条件选择 ────────────────────────┐
│ 语言：                                                     │
│ [✓] GBK / CHS / 简体                                      │
│ [ ] BIG5 / CHT / 繁体                                     │
│                                                           │
│ 清晰度：                                                   │
│ [ ] 720p                                                  │
│ [✓] 1080p                                                 │
│ [ ] 1080p 及以上                                          │
│ [ ] 2160p / 4K                                            │
│                                                           │
│ 编码：                                                     │
│ [ ] HEVC / H265                                           │
│ [ ] AVC / H264                                            │
│                                                           │
│ 自定义包含正则：                                           │
│ [ (GBK|CHS|简);(1080p)                                  ] │
│                                                           │
│ 自定义排除正则：                                           │
│ [ 720p|繁体|BIG5                                        ] │
│                                                           │
│ [保存为预设] [应用] [取消]                                │
└───────────────────────────────────────────────────────────┘
```

### 9.1 条件面板功能

| 功能 | 说明 |
|---|---|
| 勾选条件 | 自动生成条件表达式 |
| 自定义包含正则 | 只保留匹配的条目 |
| 自定义排除正则 | 排除不想要的条目 |
| 保存为预设 | 保存成顶部快捷条件 |
| 应用 | 立即刷新当前条目列表 |
| 取消 | 不修改当前条件 |

---

## 10. 常用正则规则示例

### 10.1 简体

```regex
(?i)(GBK|CHS|简|简体|SC)
```

### 10.2 繁体

```regex
(?i)(BIG5|CHT|繁|繁体|TC)
```

### 10.3 只要 1080p

```regex
(?i)\b1080p\b
```

### 10.4 1080p 及以上

```regex
(?i)(\b1080p\b|\b1080i\b|\b1440p\b|\b2160p\b|\b4k\b|\buhd\b)
```

### 10.5 排除 720p

```regex
(?i)\b720p\b
```

这个规则适合放进排除条件里。

### 10.6 简体 + 1080p 及以上

条件输入框写：

```text
(GBK|CHS|简|简体|SC);(1080p|1080i|1440p|2160p|4k|uhd)
```

### 10.7 简体 + 1080p 及以上 + 排除 720p

```text
(GBK|CHS|简|简体|SC);(1080p|1080i|1440p|2160p|4k|uhd);!(720p)
```

---

## 11. 条目列表设计

### 11.1 列表字段

条目列表建议包含：

| 字段 | 说明 |
|---|---|
| checkbox | 是否选择导出 |
| NEW 标记 | 是否为新发现条目 |
| 标题 | RSS 条目标题 |
| 订阅源 | 来自哪个 RSS |
| 清晰度 | 从标题中识别，如 720p、1080p、2160p |
| 语言 | 简、繁、未知 |
| 状态 | 已提取、无 magnet、torrent only、被规则过滤 |
| 发布时间 | RSS 条目发布时间 |
| 发现时间 | 本程序首次发现时间 |

### 11.2 新条目勾选规则

新发现的条目左侧必须有 checkbox。

默认勾选逻辑：

```text
如果条目是新发现；
并且成功提取 magnet；
并且符合当前启用的筛选规则；
并且没有导出过；
则自动勾选。
```

不自动勾选的情况：

```text
没有 magnet；
只有 torrent 链接；
被正则排除；
已经导出过；
订阅源被禁用；
条目标题为空或无法识别。
```

### 11.3 条目状态

| 状态 | 含义 |
|---|---|
| 已提取 | 成功提取 magnet |
| 无 magnet | RSS 正常，但没找到 magnet |
| torrent only | 没有 magnet，但发现 torrent 文件链接 |
| 被规则过滤 | 有 magnet，但不符合条件 |
| 已导出 | 已经复制过 |
| 解析异常 | 条目内容格式异常 |

### 11.4 条目处理状态

条目处理状态与 RSS 提取状态分开保存。`matchStatus` 只表示 magnet 提取和规则诊断，实际处理生命周期使用 `processingStatus`：

| 处理状态 | 界面指示 | 含义 |
|---|---|---|
| `pending` | 绿色圆点 | 未使用，等待用户筛选或复制 |
| `discarded` | 黄色圆点 | 暂时废弃，仍可恢复、重新筛选或复制 |
| `used` | 无圆点 | 已使用，通常表示已经复制过 |
| `deleted` | 隐藏 | 用户软删除，避免短期内被 RSS 重复抓回 |

新发现且含 magnet 的条目默认进入 `pending`。规则筛选只用于批量勾选，不阻止用户手动勾选和复制。

### 11.5 工作区视图

左侧主视图使用工作区加订阅列表：

```text
工作区
  未使用
  暂时废弃
  已使用
  异常条目
  检查失败
RSS 订阅
  [分组] 订阅名
```

选择具体 RSS 订阅时，默认只展示该订阅的 `pending` 条目。`discarded` 条目只在“暂时废弃”工作区展示，并按发现时间从新到旧排序。

### 11.6 条目搜索

主界面提供轻量搜索框：

```text
默认搜索当前左侧栏目；
可切换为全局搜索，搜索全部缓存条目；
可选择包含已删除条目，但仅在有搜索词时临时显示；
多个关键词用空格分隔，表示全部都要命中。
```

搜索范围包括：

```text
标题、订阅名、RSS 候选字段、info hash、magnet、torrent URL、提取状态和处理状态。
```

搜索不修改条目状态，也不写入配置；清空搜索后恢复当前栏目视图。

---

## 12. 剪贴板导出设计

### 12.1 默认导出方式

本项目默认不生成文件，而是直接复制到剪贴板。

复制格式：

```text
magnet:?xt=urn:btih:xxxxxx...
magnet:?xt=urn:btih:yyyyyy...
magnet:?xt=urn:btih:zzzzzz...
```

规则：

```text
一行一个 magnet；
多条之间使用回车换行分隔；
不额外添加序号；
不额外添加标题；
不添加空行。
```

### 12.2 导出按钮

主窗口底部提供：

```text
[立即刷新]
[全部勾选]
[按条件批选]
[清除勾选]
[复制勾选]
[复制全部未使用]
```

### 12.3 各按钮含义

| 按钮 | 行为 |
|---|---|
| 复制勾选 | 只复制 checkbox 勾选的条目 |
| 全部勾选 | 勾选当前侧栏范围内全部可复制 magnet，不应用规则 |
| 按条件批选 | 对当前侧栏范围建立批次，并勾选符合当前规则的 magnet |
| 清除勾选 | 清除当前侧栏范围内的 checkbox 勾选 |
| 复制全部未使用 | 复制所有 `pending` magnet |
| 低频导出 | 当前筛选、当前订阅、失败诊断等入口放入顶部菜单 |

### 12.4 复制后行为

复制成功后弹出轻提示：

```text
已复制 12 条 magnet 到剪贴板
```

同时询问或按设置自动处理：

```text
[✓] 复制后标记为已使用
[ ] 复制后从列表隐藏已导出项
```

推荐默认：

```text
复制后标记为已使用：开启
复制后隐藏已导出项：关闭
```

### 12.5 批量筛选结算

`按条件批选` 会对当前左侧范围建立一个待结算批次。表格仍显示当前范围中的全部可复制条目，符合当前规则的自动勾选，用户可以继续手动增减。

点击 `复制勾选` 时：

```text
已勾选并复制的条目标记为 used；
如果批次来源是 pending，批次中剩余未复制项标记为 discarded；
如果批次来源是 discarded，剩余未复制项继续保持 discarded；
没有活动批次时，只标记实际复制的条目，不影响其它 pending 条目。
```

同时只允许一个活动批次。活动批次保存到 `active_batch.json`，隐藏到托盘或重启后仍可继续结算或取消。

---

## 13. RSS 更新间隔设计

### 13.1 全局更新间隔

设置页面提供：

```text
全局更新间隔：[ 30 ] 分钟
启动后延迟检查：[ 10 ] 秒
失败重试间隔：[ 5 ] 分钟
```

### 13.2 单订阅更新间隔

每个 RSS 可以单独设置：

```text
[✓] 使用全局间隔
[ ] 自定义间隔：[ 60 ] 分钟
```

### 13.3 检查策略

```text
程序启动后，先读取 feeds.json；
根据每个订阅的 lastCheckedAt 和 intervalMinutes 判断是否需要检查；
检查时避免所有 RSS 同一秒并发请求；
每个订阅之间间隔 1~3 秒；
失败后进入退避重试，不频繁请求。
```

---

## 14. 网络与 RSS 状态判断

### 14.1 总体状态

托盘和主界面顶部显示总体状态：

| 状态 | 判断方式 |
|---|---|
| 正常 | 至少一个 RSS 最近检查成功，且没有严重错误 |
| 检查中 | 当前正在请求 RSS |
| 有新增 | 最近一次检查发现新 magnet |
| 部分失败 | 有些 RSS 成功，有些失败 |
| 离线 | 网络不可用或所有 RSS 请求失败 |
| 暂停 | 用户暂停了自动检查 |

### 14.2 单个 RSS 状态

每个订阅显示：

```text
最后检查时间；
下次检查时间；
HTTP 状态码；
是否成功解析 XML；
是否发现 item/entry；
是否发现 magnet；
本次新增数量；
连续失败次数。
```

### 14.3 失败原因分类

| 类型 | 提示文案 |
|---|---|
| 网络失败 | 无法连接网络或目标站点不可达 |
| DNS 失败 | 域名解析失败 |
| HTTPS 失败 | TLS/证书连接失败 |
| HTTP 异常 | 返回 403、404、500 等状态码 |
| 非 XML | 返回内容不是 RSS/Atom XML |
| XML 解析失败 | 内容像 XML，但结构异常 |
| 无条目 | RSS 正常，但没有 item/entry |
| 无 magnet | RSS 正常，但没找到 magnet |
| torrent only | 没有 magnet，但发现 torrent 链接 |
| 被条件过滤 | 有 magnet，但不符合当前规则 |

---

## 15. 托盘栏设计

### 15.1 托盘图标状态

托盘图标需要清晰表达状态。

| 状态 | 图标含义 | 悬停提示示例 |
|---|---|---|
| 正常 | 程序正常运行 | 正常｜12 个订阅｜0 个新增 |
| 检查中 | 正在刷新 RSS | 检查中｜已完成 3/12 |
| 有新增 | 发现新 magnet | 有新增｜5 条待复制 |
| 部分失败 | 有订阅检查失败 | 警告｜3 个订阅失败 |
| 离线 | 网络或全部 RSS 不可用 | 离线｜无法连接 RSS |
| 暂停 | 自动检查暂停 | 已暂停｜右键继续监控 |

### 15.2 左键点击

左键点击托盘图标：

```text
如果主窗口隐藏：打开主窗口；
如果主窗口已打开：激活到前台；
如果有新增：默认打开“未使用”列表。
```

### 15.3 右键菜单

```text
打开主界面
复制全部未使用到剪贴板
复制符合当前条件的未使用项
立即检查全部订阅
只检查失败订阅
暂停自动检查 / 继续自动检查

状态
  网络状态：正常 / 离线 / 部分失败
  RSS 状态：成功 9，失败 3
  新增：5 条
  下次检查：28 分钟后

条件
  GBK|CHS|简
  BIG5|CHT|繁
  720p
  1080p
  1080p+
  打开条件选择

设置
打开日志目录
退出
```

---

## 16. magnet 提取逻辑

### 16.1 扫描字段

不能只扫描 `<link>`，需要多字段兜底。

RSS item 中扫描：

```text
item.title
item.link
item.guid
item.description
item.content:encoded
item.enclosure/@url
item.torrent
item 下未知自定义节点全文
```

Atom entry 中扫描：

```text
entry.title
entry.link/@href
entry.id
entry.summary
entry.content
entry 下未知自定义节点全文
```

### 16.2 提取流程

```text
下载 RSS 文本；
尝试 XML 解析；
遍历 item/entry；
收集候选字段文本；
HTML 解码；
URL 解码；
正则扫描 magnet；
提取 info hash；
按 info hash 去重；
应用条件规则；
写入 item_cache.jsonl；
刷新界面列表。
```

### 16.3 magnet 正则

基础正则：

```regex
magnet:\?xt=urn:btih:[A-Za-z0-9]+[^\s<>'"]*
```

处理时还要注意：

```text
把 &amp; 还原成 &；
去除末尾多余标点；
同一条 item 里多个 magnet 时全部提取；
同一个 info hash 只保留一条。
```

---

## 17. 类似 Mikan RSS 的兼容策略

对于类似下面这种 RSS：

```text
https://mikanani.me/RSS/Classic
```

不能假设 magnet 一定在 `<link>` 中。

需要兼容：

```text
RSS 标准字段；
description 里的 HTML；
enclosure 附件链接；
torrent 相关自定义字段；
未知命名空间字段；
可能只有 torrent 链接、没有 magnet 的情况。
```

如果没有直接提取到 magnet，但发现 `.torrent` 链接，界面提示：

```text
RSS 正常，但未发现 magnet；发现 torrent 链接。
```

如果 `.torrent` URL 的文件名本身是可信格式的 BitTorrent info hash（40 位十六进制或 32 位 Base32），可以在本地直接推导：

```text
magnet:?xt=urn:btih:<info-hash>
```

此过程不下载、不解析 torrent 文件。无法从 URL 可靠推导时仍只记录 `torrent only` 状态，避免把功能变重。

对于公开的 Mikan Classic RSS，还允许在首次检查、补抓目标增大或用户手动触发时，请求：

```text
/Home/Classic/{page}
```

分页 HTML 只用于补充公开历史条目中的标题、发布时间、magnet 和 torrent URL。补抓达到“每个订阅最大保留文章数”后停止；日常定时检查仍优先使用标准 RSS。此受限适配不会下载或解析 torrent 文件，也不会推广为任意站点网页抓取。

---

## 18. 本地数据结构

### 18.1 feeds.json

```json
[
  {
    "id": "feed_mikan_classic",
    "name": "Mikan Classic",
    "url": "https://mikanani.me/RSS/Classic",
    "enabled": true,
    "group": "默认",
    "useGlobalInterval": true,
    "intervalMinutes": 30,
    "defaultRuleId": "rule_1080p_sc",
    "autoCheckNewMatchedItems": true,
    "enableMikanHistoryBackfill": true
  }
]
```

### 18.2 rules.json

```json
[
  {
    "id": "rule_1080p_sc",
    "name": "简体 1080p 及以上",
    "includeExpression": "(GBK|CHS|简|简体|SC);(1080p|1080i|1440p|2160p|4k|uhd)",
    "excludeExpression": "720p|BIG5|CHT|繁|繁体",
    "enabled": true,
    "showAsQuickButton": true
  }
]
```

### 18.3 app.settings.json

```json
{
  "globalIntervalMinutes": 30,
  "startupCheckDelaySeconds": 10,
  "failedRetryMinutes": 5,
  "autoStartWithWindows": false,
  "startMinimizedToTray": true,
  "copyAfterActionMarkExported": true,
  "hideExportedAfterCopy": false,
  "maxCacheItems": 10000,
  "maxArticlesPerFeed": 1000,
  "keepHistoryDays": 90,
  "showOnlyMatchingItems": true,
  "clipboardLineEnding": "CRLF"
}
```

### 18.4 item_cache.jsonl

一行一个 JSON 对象，便于追加写入。

```json
{"id":"item_001","feedId":"feed_mikan_classic","title":"Example 1080p CHS","magnet":"magnet:?xt=urn:btih:xxxx","infoHash":"xxxx","publishedAt":"2026-06-01T10:00:00+08:00","foundAt":"2026-06-01T10:02:00+08:00","isNew":true,"isChecked":false,"isExported":false,"matchStatus":"extracted","processingStatus":"pending"}
```

### 18.4.1 active_batch.json

活动批选批次保存为单个 JSON 对象：

```json
{
  "isActive": true,
  "id": "batch_xxx",
  "sourceMode": "Pending",
  "feedId": null,
  "sourceProcessingStatus": "pending",
  "selectionMode": "rule_matched",
  "createdAt": "2026-06-03T10:00:00+08:00",
  "itemIds": ["item_001", "item_002"],
  "originalCheckedByItemId": {
    "item_001": false,
    "item_002": true
  }
}
```

没有活动批次时写入空批次对象，避免缺文件导致兼容问题。

### 18.5 feed_state.json

```json
{
  "feed_mikan_classic": {
    "lastCheckedAt": "2026-06-01T10:00:00+08:00",
    "nextCheckAt": "2026-06-01T10:30:00+08:00",
    "lastStatus": "ok",
    "httpStatusCode": 200,
    "lastNewCount": 5,
    "lastMagnetCount": 8,
    "lastRssEntryCount": 100,
    "lastHistoryBackfillEntryCount": 1000,
    "completedHistoryBackfillTarget": 1000,
    "lastHistoryBackfillAt": "2026-06-01T10:00:20+08:00",
    "historyBackfillWarning": "",
    "consecutiveFailCount": 0,
    "lastError": ""
  }
}
```

---

## 19. 程序模块架构

```text
RSSMagnetCatcher/
├─ App/
│  ├─ Program.cs
│  ├─ TrayApplicationContext.cs
│  └─ AppBootstrapper.cs
├─ UI/
│  ├─ MainForm.cs
│  ├─ SettingsForm.cs
│  ├─ FeedEditForm.cs
│  ├─ RulePickerForm.cs
│  └─ DiagnosticsForm.cs
├─ Core/
│  ├─ Models/
│  │  ├─ FeedConfig.cs
│  │  ├─ FeedState.cs
│  │  ├─ RssItem.cs
│  │  ├─ MagnetItem.cs
│  │  └─ FilterRule.cs
│  ├─ Services/
│  │  ├─ FeedScheduler.cs
│  │  ├─ RssFetchService.cs
│  │  ├─ RssParseService.cs
│  │  ├─ MagnetExtractService.cs
│  │  ├─ RuleMatchService.cs
│  │  ├─ ClipboardExportService.cs
│  │  └─ NotificationService.cs
│  └─ Utils/
│     ├─ RegexHelper.cs
│     ├─ HashHelper.cs
│     └─ TimeHelper.cs
├─ Storage/
│  ├─ JsonConfigStore.cs
│  ├─ JsonlItemStore.cs
│  ├─ FeedStateStore.cs
│  └─ ExportHistoryStore.cs
└─ Infrastructure/
   ├─ Logger.cs
   ├─ StartupManager.cs
   └─ NetworkDiagnostics.cs
```

---

## 20. 核心流程

### 20.1 自动检查流程

```text
后台定时器触发；
读取启用的 RSS 订阅；
判断是否到达检查时间；
请求 RSS；
解析 XML；
提取条目；
扫描 magnet；
按 info hash 去重；
应用规则；
符合条件的新条目自动勾选；
写入缓存；
刷新主界面和托盘状态；
必要时弹出通知。
```

### 20.2 手动复制流程

```text
用户点击“复制勾选”；
程序读取当前勾选条目；
过滤掉无 magnet 的条目；
按发现时间或发布时间排序；
拼接为一行一个 magnet；
写入剪贴板；
提示复制数量；
按设置标记为已导出。
```

### 20.3 条件筛选流程

```text
用户点击快捷条件或打开条件选择；
程序生成 include / exclude 表达式；
重新扫描当前列表；
符合规则的条目显示为可复制；
新发现且符合规则的条目自动勾选；
不符合规则的条目标记为“被规则过滤”。
```

---

## 21. 设置页面

设置页面包含：

```text
基础设置
  [ ] 开机自动启动
  [✓] 启动后进入托盘
  [✓] 关闭窗口时最小化到托盘

检查设置
  全局更新间隔：[30] 分钟
  失败重试间隔：[5] 分钟
  每个 RSS 请求间隔：[2] 秒
  每个订阅最大保留文章数：[1000]

导出设置
  [✓] 默认复制到剪贴板
  [✓] 复制后标记为已使用（当前版本固定）
  [ ] 复制后隐藏已导出项
  换行格式：[Windows CRLF]

规则设置
  默认条件：[简体 1080p 及以上]
  [管理条件预设]

缓存设置
  最大缓存条目：[10000]
  历史保留天数：[90]
  [清理已导出历史]
  [打开 data 文件夹]
```

---

## 22. MVP 开发优先级

### 第一阶段：可用版

```text
WinForms 主窗口；
托盘常驻；
添加 RSS；
手动检查 RSS；
提取 magnet；
条目 checkbox；
复制勾选到剪贴板；
JSON/JSONL 存储。
```

### 第二阶段：好用版

```text
自动定时检查；
托盘状态；
新条目提醒；
全部未使用复制；
条件栏；
快捷条件按钮；
正则 include/exclude。
```

### 第三阶段：完善版

```text
条件选择面板；
单 RSS 更新间隔；
失败诊断；
按订阅复制；
缓存清理；
日志查看；
开机自启。
```

---

## 23. 验收标准

项目完成后至少满足：

```text
1. 可以添加多个 RSS 地址；
2. 可以设置全局更新间隔；
3. 可以为单个 RSS 设置独立更新间隔；
4. 程序可以长期保持在托盘后台；
5. 托盘能显示正常、检查中、有新增、失败、离线、暂停状态；
6. 可以判断 RSS 是否连接正常；
7. RSS 正常但无 magnet 时能明确提示；
8. 新发现条目左侧有 checkbox；
9. 符合规则的新条目可以自动勾选；
10. 可以通过条件栏快速筛选 GBK/CHS/简、BIG5/CHT/繁、720p、1080p 等；
11. 可以使用正则表达式限制只要 1080p 及以上；
12. 可以复制勾选 magnet 到剪贴板；
13. 可以复制全部未使用 magnet 到剪贴板；
14. 多条 magnet 复制时使用回车换行分隔；
15. 所有配置和缓存都保存在程序 data 文件夹内，不依赖数据库。
```

---

## 24. 最终推荐实现结论

最终采用：

```text
C# + .NET + WinForms + NotifyIcon + JSON/JSONL + Clipboard
```

---

## 25. 用户主动种子导出补充

在保留剪贴板 magnet 导出作为默认能力的基础上，允许用户主动选择“导出种子”：

```text
复制磁力成功，或种子文件保存成功，二者任选其一都视为该条目已使用。
```

种子导出仅保存 RSS 或公开历史页中已经提供的 `.torrent` URL：

```text
保存目录：data/torrent_exports/yyyyMMddHHmmss/
文件夹创建在便携 data 目录内；
全部选中种子保存完成后，再打开该文件夹给用户查看；
失败或缺少 torrent URL 的条目不标记为已使用。
```

此功能是受限例外：

```text
不解析 torrent 文件；
不自动下载媒体内容；
不接入 qBittorrent、Transmission 或其他下载器；
不在后台自动导出种子，必须由用户主动触发。
```
