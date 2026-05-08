# Zhengyan.DigitalWife.Tools.ModelInstaller

`Zhengyan.DigitalWife.Tools.ModelInstaller` 是仓库内部使用的模型下载与解压工具，主要被 `scripts/download-models.ps1` 和 `scripts/download-models.sh` 调用。

## 典型命令

下载单个文件：

```powershell
dotnet run --project tools/Zhengyan.DigitalWife.Tools.ModelInstaller/Zhengyan.DigitalWife.Tools.ModelInstaller.csproj -- download-file <url> <destination>
```

下载并解压 `tar.bz2`：

```powershell
dotnet run --project tools/Zhengyan.DigitalWife.Tools.ModelInstaller/Zhengyan.DigitalWife.Tools.ModelInstaller.csproj -- download-and-extract-tarbz2 <url> <targetDirectory> [downloadsRoot]
```
