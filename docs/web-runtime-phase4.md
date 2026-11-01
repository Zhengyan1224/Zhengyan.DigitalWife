# Web Runtime 阶段 4：Web 包和资源清单

> 状态：**已实现**
>
> 完成日期：2026-10-31
>
> 对应设计文档：`docs/web-runtime-design.md` 的阶段 4

## 1. 本阶段目标

阶段 4 把阶段 3 的 Web 专用资源读取逻辑提升为可复用的资产边界。静态开发目录、发布目录和未来的内存包都通过统一的 `IGameAssetStore` 读取资源；Web 清单负责声明运行时、渲染器、入口场景、资源大小、哈希和脚本程序集。

## 2. 已实现内容

### 共享资产接口

新增 `GamePlayer.Runtime.IGameAssetStore`：

- `ExistsAsync`
- `OpenReadAsync`
- `ReadAllBytesAsync`
- `GetUriAsync`

共享场景和解析器可以依赖这个接口，不需要直接使用 `File.*`、Windows 盘符或浏览器特定 API。

新增 `MemoryAssetStore`：

- 用规范化的相对路径保存内存字节；
- 读取时返回副本，避免调用方修改缓存；
- 拒绝 `.`、`..` 和空路径；
- 提供 `memory:///...` URI；
- 作为未来 `.dwgame` 解包结果的存储边界。

### Web 静态包

`WebHttpAssetStore` 现在实现 `IGameAssetStore`，并加强 `web.manifest.json` 校验：

- `formatVersion` 必须受支持；
- `runtime` 必须为 `web`；
- `renderer` 必须为 `webgl2`；
- `apiVersion` 不能为空；
- `entryScene` 必须登记在资源列表中；
- 资源路径不能重复、不能逃逸根目录；
- 脚本 id 不能重复，脚本程序集必须登记为资源；
- 资源读取校验文件大小和 SHA-256。

新增 `WebAssetLoadException`，资源错误会保留：

- manifest 中的逻辑资源路径；
- 实际请求 URI；
- 请求失败、尺寸不匹配或哈希不匹配的原因。

### 开发目录与发布目录

阶段 3 的 `wwwroot/web-game/` 继续作为静态包源目录。发布后同一目录会出现在 `publish/wwwroot/web-game/`，manifest 中的四个资源在源目录和发布目录的大小与 SHA-256 均一致。

## 3. 当前 manifest 格式

```json
{
  "formatVersion": 1,
  "runtime": "web",
  "renderer": "webgl2",
  "apiVersion": "1.0",
  "entryScene": "scenes/main.scene.json",
  "assets": [
    {
      "path": "assets/models/minimal.pmx",
      "kind": "pmx",
      "size": 487,
      "sha256": "..."
    }
  ],
  "scripts": [],
  "capabilities": ["scene", "pmx-static", "vmd-camera", "webgl2"]
}
```

脚本清单已经有结构校验；阶段 5 在此基础上增加 Web 程序集下载、依赖顺序、ABI 和运行生命周期。

## 4. 已实现内容

- `WebDwGamePackageLoader` 在内存中读取未加密 `.dwgame`，安全读取 ZIP 条目并填充 `WebMemoryAssetStore`；
- 浏览器文件选择器可以把 `.dwgame` 读入内存并启动 Web 场景；
- `WebIndexedDbAssetCache` 和 `WebCachedAssetStore` 提供带大小/SHA-256 校验的持久缓存；
- manifest 支持 RSA-SHA256 签名字段和统一规范化校验；
- `WebGamePackageExporter` 生成静态 Web manifest，保留资源大小、SHA-256 和 Web 脚本条目；
- `web-storage.js` 提供 IndexedDB 的写入、读取、删除和清空操作。

加密 `.dwgame` 仍由原生 PC/Android 播放器处理；浏览器端需要先导出未加密 Web 包，避免在 WASM 中引入平台不支持的 `AesGcm`。

## 5. 验证记录

已执行：

```text
dotnet build src/Zhengyan.DigitalWife.GamePlayer.Web/Zhengyan.DigitalWife.GamePlayer.Web.csproj --no-restore -p:NuGetAudit=false -v minimal
node --check src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-runtime.js
node --check src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/webgl2-renderer.js
dotnet run --project tests/Zhengyan.DigitalWife.PmxParity.Tests --no-restore
```

开发服务器验证了以下资源均返回 HTTP 200：manifest、工程 JSON、场景 JSON、PMX、VMD 和 WebGL2 renderer 模块。源目录和发布目录的 manifest 哈希校验通过。

目标项目构建没有错误；NuGet 漏洞数据服务不可达时会产生 `NU1900` 网络警告。当前环境没有真实浏览器，因此阶段 2/3 的 GPU 视觉验证仍保持未勾选。

## 6. 阶段状态

阶段 4 已完成。阶段 5 使用 `IWebAssetStore` 加载预编译 Web 程序集；Web 端不运行 Roslyn `.csx` 编译，也不修改 PC/Android 的脚本行为。
