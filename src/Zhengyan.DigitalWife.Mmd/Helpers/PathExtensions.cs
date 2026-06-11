namespace Zhengyan.DigitalWife.Mmd.Helpers;
public static class PathExtensions
{
    public static string FormatFilePath(this string filePath)
    {
        if (Path.DirectorySeparatorChar == '\\')
        {
            filePath = filePath.Replace('/', '\\');
        }
        else
        {
            filePath = filePath.Replace('\\', '/');
        }

        return filePath;
    }
}

