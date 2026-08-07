import { downloadFile } from '@/services/FileStorageService';

/**
 * 把服务端文件存到本地。
 *
 * 不能用 <a href> 或 window.open 直连 —— 接口要 Authorization header，
 * 浏览器原生导航带不上 JWT。只能 fetch 成 Blob 再造一个 objectURL 触发保存。
 * 代价是整个文件要先进内存，超大文件请用别的手段。
 */
export async function downloadToDisk(
  path: string,
  drives: string,
  fileName: string,
): Promise<void> {
  const res = await downloadFile(path, drives);

  // 后端校验失败时 ResultFilter 会返回 200 + JSON，此时拿到的是对象不是 Blob
  if (!(res instanceof Blob)) {
    throw new Error(res?.message || '下载失败');
  }

  const url = URL.createObjectURL(res);
  try {
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.rel = 'noopener';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  } finally {
    // 立即 revoke 在 Firefox 上会中断保存，延迟释放
    window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
  }
}
