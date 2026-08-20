import type { ReactNode } from "react";
import { AnimatePresence, motion } from "motion/react";
import { EASE } from "@/components/motion";
import { cn } from "@/lib/utils";

export function ExpandablePanel({
  open,
  className,
  children,
}: {
  open: boolean;
  className?: string;
  children: ReactNode;
}) {
  return (
    <AnimatePresence initial={false}>
      {open ? (
        <motion.div
          key="expand"
          initial={{ height: 0, opacity: 0 }}
          animate={{ height: "auto", opacity: 1 }}
          exit={{ height: 0, opacity: 0 }}
          transition={{ duration: 0.22, ease: EASE.out }}
          className="overflow-hidden"
        >
          <div className={cn("border-t border-border/50", className)}>{children}</div>
        </motion.div>
      ) : null}
    </AnimatePresence>
  );
}
