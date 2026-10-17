using System.Buffers;
using Serva.Core.Localization;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary><see cref="SyncEngine"/> 的底层文件操作（partial）。</summary>
public sealed partial class SyncEngine
{
    /// <summary>把相对路径（<c>/</c> 分隔）拼到根目录上。</summary>
    internal static string Combine(string root, string relativePath) =>
        relativePath.Length == 0
            ? root
            : Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// 复制单个文件：写入 <c>&lt;目标&gt;.serva-part</c> → 校验 SHA-256 → 原子改名 → 恢复时间戳。
    /// <para>这样绝不会留下「半截文件被误判为已存在且相同」的情况。</para>
    /// </summary>
    private static async Task CopyFileAsync(
        string source,
        string target,
        SyncOptions options,
        Action<long> onBytes,
        CancellationToken cancellationToken)
    {
        string? targetDirectory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        string partPath = target + PartSuffix;

        try
        {
            await using (FileStream input = new(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream output = new(
                partPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous))
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                try
                {
                    int read;
                    while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        onBytes(read);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            if (options.VerifyAfterCopy)
            {
                byte[] sourceHash = await ComputeSha256Async(source, cancellationToken).ConfigureAwait(false);
                byte[] partHash = await ComputeSha256Async(partPath, cancellationToken).ConfigureAwait(false);

                if (!sourceHash.AsSpan().SequenceEqual(partHash))
                {
                    throw new IOException(CoreText.Format(
                        "Verification after copy failed: SHA-256 mismatch (source {0} / target {1})",
                        "复制后校验失败：SHA-256 不一致（源 {0} / 目标 {1}）",
                        Convert.ToHexString(sourceHash),
                        Convert.ToHexString(partHash)));
                }
            }

            if (File.Exists(target))
            {
                FileAttributes existing = File.GetAttributes(target);
                if ((existing & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(target, existing & ~FileAttributes.ReadOnly);
                }
            }

            File.Move(partPath, target, overwrite: true);

            // 时间戳与属性始终保留（与源文件一致），不做开关。
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
            try
            {
                File.SetAttributes(target, File.GetAttributes(source) & ~FileAttributes.Directory);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        catch
        {
            TryDeleteFile(partPath);
            throw;
        }
    }

    /// <summary>流式计算文件 SHA-256（边读边算，不整体载入内存）。</summary>
    private static async Task<byte[]> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var sha = System.Security.Cryptography.SHA256.Create();
        return await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 删除目标端多余文件：默认移入 Windows 系统回收站（可从回收站还原）；
    /// 关闭该选项时直接永久删除。
    /// </summary>
    private static void DeleteItem(string path, SyncOptions options, string relativePath, Journal journal)
    {
        if (!File.Exists(path))
        {
            return;
        }

        if (options.UseTrashFolder)
        {
            if (!RecycleBin.TryDelete(path))
            {
                // 回收站不可用（无权限 / 系统策略关闭）：不静默永久删除，保留文件并记为失败。
                throw new IOException(CoreText.Get(
                    "Cannot move the extra file to the Windows Recycle Bin; the file was kept.",
                    "无法把多余文件移入系统回收站，已保留该文件。"));
            }

            journal.Append(
                JournalOperationKind.Delete, relativePath, path, string.Empty, 0, true,
                CoreText.Get("Moved to the Windows Recycle Bin", "移入系统回收站"));
            return;
        }

        File.Delete(path);
        journal.Append(JournalOperationKind.Delete, relativePath, path, string.Empty, 0, true);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
