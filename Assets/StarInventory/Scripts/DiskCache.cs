using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public sealed class DiskCache
{
    private readonly string rootDir;

    public DiskCache(string rootDir)
    {
        this.rootDir = rootDir;
        Directory.CreateDirectory(rootDir);
    }

    public string GetPath(IconKey key)
    {
        string vDir = Path.Combine(rootDir, Sanitize(key.version));
        string rDir = Path.Combine(vDir, key.resolution.ToString());
        Directory.CreateDirectory(rDir);

        string hash = Sha1Hex(key.ToString());
        return Path.Combine(rDir, $"{hash}.png");
    }

    public async Task<byte[]> ReadAsync(string path)
    {
        if (!File.Exists(path)) return null;
        return await Task.Run(() => File.ReadAllBytes(path)).ConfigureAwait(false);
    }

    public async Task WriteAsync(string path, byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return;

        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await Task.Run(() => File.WriteAllBytes(path, bytes)).ConfigureAwait(false);
    }

    private static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "v0";
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    private static string Sha1Hex(string s)
    {
        using var sha1 = SHA1.Create();
        byte[] data = Encoding.UTF8.GetBytes(s);
        byte[] hash = sha1.ComputeHash(data);
        var sb = new StringBuilder(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
        return sb.ToString();
    }
}
