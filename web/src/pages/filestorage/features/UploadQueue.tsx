import React from 'react';
import { CheckCircle2, RefreshCw, X, XCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ProgressBar } from '@/components/ui/progress-bar';
import type { UploadTask } from '../hooks/useUpload';

function formatSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return '-';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`;
}

function statusText(task: UploadTask): string {
  switch (task.status) {
    case 'pending':
      return '排队中';
    case 'uploading':
      return task.totalChunks > 1
        ? `${task.uploadedChunks}/${task.totalChunks} 片 · ${task.progress}%`
        : `${formatSize(task.size)}`;
    case 'merging':
      return '合并中';
    case 'success':
      return formatSize(task.size);
    case 'canceled':
      return '已取消';
    case 'error':
      return '失败';
    default:
      return '';
  }
}

type UploadQueueProps = {
  tasks: UploadTask[];
  onCancel: (id: string) => void;
  onRetry: (id: string) => void;
  onClear: () => void;
};

/**
 * 上传队列，挂在左侧面板底部。
 *
 * 刻意不用 sonner：toast 会自动消失、会堆叠，和「确定进度 + 多条目 + 每行带
 * 取消/重试按钮」的模型冲突；而且它浮在右下角，恰好挡住刚拖拽的那棵树。
 */
export const UploadQueue: React.FC<UploadQueueProps> = ({
  tasks,
  onCancel,
  onRetry,
  onClear,
}) => {
  if (tasks.length === 0) return null;

  const finishedCount = tasks.filter(
    (t) => t.status === 'success' || t.status === 'canceled',
  ).length;

  return (
    <div className="fs-upload-queue custom-scrollbar">
      <div className="fs-upload-queue-head">
        <span>上传队列（{tasks.length}）</span>
        {finishedCount > 0 && (
          <Button variant="ghost" size="sm" className="h-6 px-2 text-xs" onClick={onClear}>
            清除已完成
          </Button>
        )}
      </div>

      {tasks.map((task) => {
        const active =
          task.status === 'pending' ||
          task.status === 'uploading' ||
          task.status === 'merging';

        return (
          <div key={task.id} className="fs-upload-row">
            <div className="fs-upload-head">
              {task.status === 'success' && (
                <CheckCircle2 className="fs-upload-status-icon fs-upload-ok" />
              )}
              {task.status === 'error' && (
                <XCircle className="fs-upload-status-icon fs-upload-fail" />
              )}
              <span className="fs-upload-name" title={task.fileName}>
                {task.fileName}
              </span>
              <span className="fs-upload-meta">{statusText(task)}</span>

              {active && (
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-6 w-6"
                  aria-label="取消上传"
                  onClick={() => onCancel(task.id)}
                >
                  <X className="h-3.5 w-3.5" />
                </Button>
              )}

              {(task.status === 'error' || task.status === 'canceled') && (
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-6 w-6"
                  aria-label="重新上传"
                  onClick={() => onRetry(task.id)}
                >
                  <RefreshCw className="h-3.5 w-3.5" />
                </Button>
              )}
            </div>

            {active && <ProgressBar value={task.progress} size="sm" />}

            {task.error && <div className="fs-upload-error">{task.error}</div>}
          </div>
        );
      })}
    </div>
  );
};
