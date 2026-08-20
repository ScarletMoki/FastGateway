import { useState, useCallback, memo } from "react";
import { motion, AnimatePresence } from "motion/react";
import { Check, Copy } from "lucide-react";
import { Button, type ButtonProps } from "@/components/ui/button";
import { toast } from "sonner";
import { cn } from "@/lib/utils";

export interface CopyButtonProps extends Omit<ButtonProps, "onClick"> {
  text: string;
  successMessage?: string;
  onCopySuccess?: () => void;
}

export const CopyButton = memo(function CopyButton({
  text,
  successMessage = "已复制到剪贴板",
  onCopySuccess,
  className,
  variant = "ghost",
  size = "icon",
  ...props
}: CopyButtonProps) {
  const [copied, setCopied] = useState(false);

  const handleCopy = useCallback(
    async (e: React.MouseEvent) => {
      e.stopPropagation();
      e.preventDefault();
      const val = text?.trim();
      if (!val) return;

      try {
        if (navigator?.clipboard?.writeText) {
          await navigator.clipboard.writeText(val);
        } else {
          const textarea = document.createElement("textarea");
          textarea.value = val;
          textarea.style.position = "fixed";
          textarea.style.opacity = "0";
          document.body.appendChild(textarea);
          textarea.select();
          document.execCommand("copy");
          document.body.removeChild(textarea);
        }
        setCopied(true);
        if (successMessage) toast.success(successMessage);
        onCopySuccess?.();
        setTimeout(() => setCopied(false), 1600);
      } catch {
        toast.error("复制失败，请手动选择复制");
      }
    },
    [text, successMessage, onCopySuccess]
  );

  return (
    <Button
      variant={variant}
      size={size}
      className={cn("relative shrink-0 text-muted-foreground hover:text-foreground", className)}
      onClick={handleCopy}
      title={copied ? "已复制" : "复制"}
      {...props}
    >
      <AnimatePresence mode="wait" initial={false}>
        {copied ? (
          <motion.span
            key="check"
            initial={{ scale: 0.5, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            exit={{ scale: 0.5, opacity: 0 }}
            transition={{ type: "spring", visualDuration: 0.18, bounce: 0.2 }}
            className="flex items-center justify-center text-emerald-500"
          >
            <Check className="h-3.5 w-3.5" />
          </motion.span>
        ) : (
          <motion.span
            key="copy"
            initial={{ scale: 0.8, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            exit={{ scale: 0.8, opacity: 0 }}
            transition={{ duration: 0.12 }}
            className="flex items-center justify-center"
          >
            <Copy className="h-3.5 w-3.5" />
          </motion.span>
        )}
      </AnimatePresence>
    </Button>
  );
});
