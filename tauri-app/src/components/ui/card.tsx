import { cn } from "../../lib/cn";
import type { HTMLAttributes } from "react";

export function Card({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn("rounded-xl border border-white/10 bg-white/5 p-4 shadow-sm", className)}
      {...props}
    />
  );
}
