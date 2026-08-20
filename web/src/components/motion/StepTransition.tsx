import { memo, type ReactNode } from "react";
import { motion, AnimatePresence } from "motion/react";
import { EASE } from "./tokens";

export interface StepTransitionProps {
  currentStep: number | string;
  direction?: "forward" | "backward";
  className?: string;
  children: ReactNode;
}

const variants = {
  enter: (direction: "forward" | "backward") => ({
    x: direction === "forward" ? 20 : -20,
    opacity: 0,
    filter: "blur(2px)",
  }),
  center: {
    x: 0,
    opacity: 1,
    filter: "blur(0px)",
  },
  exit: (direction: "forward" | "backward") => ({
    x: direction === "forward" ? -20 : 20,
    opacity: 0,
    filter: "blur(2px)",
  }),
};

export const StepTransition = memo(function StepTransition({
  currentStep,
  direction = "forward",
  className,
  children,
}: StepTransitionProps) {
  return (
    <div className="relative w-full overflow-hidden">
      <AnimatePresence mode="wait" custom={direction} initial={false}>
        <motion.div
          key={currentStep}
          custom={direction}
          variants={variants}
          initial="enter"
          animate="center"
          exit="exit"
          transition={{ duration: 0.2, ease: EASE.out }}
          className={className}
        >
          {children}
        </motion.div>
      </AnimatePresence>
    </div>
  );
});
