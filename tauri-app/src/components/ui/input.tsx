import { cn } from "../../lib/cn";
import type { InputHTMLAttributes } from "react";

export function Input({ className, ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      className={cn(
        "w-full rounded-md border border-white/15 bg-black/30 px-3 py-1.5 text-sm outline-none focus:border-brand",
        className,
      )}
      {...props}
    />
  );
}
