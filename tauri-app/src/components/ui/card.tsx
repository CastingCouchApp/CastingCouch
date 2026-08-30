import { cn } from "../../lib/cn";
import type { HTMLAttributes } from "react";

export function Card({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn("rounded-xl border border-border bg-card p-4 text-text shadow-sm", className)}
      {...props}
    />
  );
}
