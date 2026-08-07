import { useCallback, useRef, useState } from 'react';
import { toast } from 'sonner';
import {
  abortUpload,
  mergeFileChunks,
  uploadFile,
  uploadFileChunk,
} from '@/services/FileStorageService';

export const CHUNK_SIZE = 5 * 1024 * 1024;
/**
 * 超过这个大小走分片。管理端由 CreateSlimBuilder 创建且未覆盖 Kestrel
 * MaxRequestBodySize，默认上限 30MB —— 大文件必须分片，不是可选项。
 */
export const CHUNK_THRESHOLD = 5 * 1024 * 1024;
const MAX_CHUNK_RETRIES = 3;

export type UploadStatus =
  | 'pending'
  | 'uploading'
  | 'merging'
  | 'success'
  | 'error'
  | 'canceled';

export type UploadTask = {
  /** uploadId，同时作为 React key 与服务端暂存目录名 */
  id: string;
  file: File;
  fileName: string;
  /** 目标目录，不含文件名 */
  targetPath: string;
  drives: string;
  size: number;
  /** 1 表示走整文件 upload 接口 */
  totalChunks: number;
  uploadedChunks: number;
  /** 0-100 */
  progress: number;
  status: UploadStatus;
  error?: string;
};

export type UseUploadOptions = {
  /** 单个文件成功后回调，用于刷新目录 */
  onFileDone?: (task: UploadTask) => void;
};

class UploadCanceled extends Error {}

const sleep = (ms: number) => new Promise<void>((resolve) => window.setTimeout(resolve, ms));

const createUploadId = () =>
  (
    globalThis.crypto?.randomUUID?.() ??
    `${Date.now()}-${Math.random().toString(16).slice(2)}`
  ).replace(/[^a-zA-Z0-9-]/g, '');

export function useUpload({ onFileDone }: UseUploadOptions = {}) {
  const [tasks, setTasks] = useState<UploadTask[]>([]);

  const queueRef = useRef<UploadTask[]>([]);
  const runningRef = useRef(false);
  const canceledRef = useRef<Set<string>>(new Set());
  const abortRef = useRef<Map<string, AbortController>>(new Map());
  const doneRef = useRef(onFileDone);
  doneRef.current = onFileDone;

  const patchTask = useCallback((id: string, patch: Partial<UploadTask>) => {
    setTasks((prev) => prev.map((t) => (t.id === id ? { ...t, ...patch } : t)));
  }, []);

  const uploadOne = useCallback(
    async (task: UploadTask) => {
      patchTask(task.id, {
        status: 'uploading',
        progress: 0,
        uploadedChunks: 0,
        error: undefined,
      });

      try {
        // 小文件走单请求，不产生服务端暂存目录
        if (task.totalChunks <= 1) {
          const res = await uploadFile(task.file, task.targetPath, task.drives);
          if (!res?.success) throw new Error(res?.message || '上传失败');
          patchTask(task.id, { progress: 100, uploadedChunks: 1, status: 'success' });
          doneRef.current?.(task);
          return;
        }

        // 分片严格串行：保证进度单调、失败即停、内存里同时只有一片
        for (let index = 0; index < task.totalChunks; index += 1) {
          if (canceledRef.current.has(task.id)) throw new UploadCanceled();

          const start = index * CHUNK_SIZE;
          const chunk = task.file.slice(start, Math.min(start + CHUNK_SIZE, task.size));

          let lastError: unknown;
          let ok = false;

          // 单片重试，网络抖动不用整个文件重来
          for (let attempt = 0; attempt <= MAX_CHUNK_RETRIES; attempt += 1) {
            const controller = new AbortController();
            abortRef.current.set(task.id, controller);
            try {
              const res = await uploadFileChunk(
                chunk,
                task.targetPath,
                task.drives,
                task.id,
                index,
                task.totalChunks,
                controller.signal,
              );
              if (!res?.success) {
                throw new Error(res?.message || `第 ${index + 1} 片上传失败`);
              }
              ok = true;
              break;
            } catch (error) {
              lastError = error;
              if (canceledRef.current.has(task.id)) throw new UploadCanceled();
              if (attempt < MAX_CHUNK_RETRIES) await sleep(500 * 2 ** attempt);
            } finally {
              abortRef.current.delete(task.id);
            }
          }

          if (!ok) {
            throw lastError instanceof Error ? lastError : new Error('分片上传失败');
          }

          const uploaded = index + 1;
          patchTask(task.id, {
            uploadedChunks: uploaded,
            // 留 2% 给合并阶段，避免进度条先到 100% 再干等
            progress: Math.round((uploaded / task.totalChunks) * 98),
          });
        }

        patchTask(task.id, { status: 'merging', progress: 99 });

        const merged = await mergeFileChunks(
          task.targetPath,
          task.drives,
          task.fileName,
          task.id,
          task.totalChunks,
        );
        if (!merged?.success) throw new Error(merged?.message || '合并失败');

        patchTask(task.id, { status: 'success', progress: 100 });
        doneRef.current?.(task);
      } catch (error) {
        const canceled = error instanceof UploadCanceled || canceledRef.current.has(task.id);
        const message = error instanceof Error ? error.message : String(error);

        // 取消或失败都通知服务端清掉暂存分片。best effort —— 失败了还有 24h 兜底清扫
        if (task.totalChunks > 1) {
          void abortUpload(task.targetPath, task.drives, task.id).catch(() => undefined);
        }

        patchTask(task.id, {
          status: canceled ? 'canceled' : 'error',
          error: canceled ? undefined : message,
        });

        if (!canceled) {
          toast.error(`${task.fileName} 上传失败: ${message}`);
        }
      }
    },
    [patchTask],
  );

  const drain = useCallback(async () => {
    if (runningRef.current) return;
    runningRef.current = true;
    try {
      while (queueRef.current.length > 0) {
        const task = queueRef.current.shift()!;
        if (canceledRef.current.has(task.id)) {
          patchTask(task.id, { status: 'canceled' });
          continue;
        }
        // 文件之间也串行
        await uploadOne(task);
      }
    } finally {
      runningRef.current = false;
    }
  }, [uploadOne, patchTask]);

  const enqueue = useCallback(
    (files: File[], targetPath: string, drives: string) => {
      const created: UploadTask[] = files.map((file) => ({
        id: createUploadId(),
        file,
        fileName: file.name,
        targetPath,
        drives,
        size: file.size,
        totalChunks: file.size > CHUNK_THRESHOLD ? Math.ceil(file.size / CHUNK_SIZE) : 1,
        uploadedChunks: 0,
        progress: 0,
        status: 'pending',
      }));

      setTasks((prev) => [...prev, ...created]);
      queueRef.current.push(...created);
      void drain();
      return created;
    },
    [drain],
  );

  const cancelTask = useCallback((id: string) => {
    canceledRef.current.add(id);
    abortRef.current.get(id)?.abort();
  }, []);

  /** 整任务重试。旧暂存目录已被清理，必须换新 uploadId */
  const retryTask = useCallback(
    (id: string) => {
      setTasks((prev) => {
        const old = prev.find((t) => t.id === id);
        if (!old) return prev;
        const next: UploadTask = {
          ...old,
          id: createUploadId(),
          uploadedChunks: 0,
          progress: 0,
          status: 'pending',
          error: undefined,
        };
        queueRef.current.push(next);
        void drain();
        return prev.filter((t) => t.id !== id).concat(next);
      });
    },
    [drain],
  );

  const clearFinished = useCallback(() => {
    setTasks((prev) => prev.filter((t) => t.status !== 'success' && t.status !== 'canceled'));
  }, []);

  const activeCount = tasks.filter(
    (t) => t.status === 'pending' || t.status === 'uploading' || t.status === 'merging',
  ).length;

  return { tasks, activeCount, enqueue, cancelTask, retryTask, clearFinished };
}
