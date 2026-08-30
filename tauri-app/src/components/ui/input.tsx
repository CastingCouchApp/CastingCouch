import { cn } from "../../lib/cn";
import type { InputHTMLAttributes } from "react";

export function Input({ className, ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      className={cn(
        "w-full rounded-md border border-border bg-input px-3 py-1.5 text-sm text-text outline-none focus:border-brand",
        className,
      )}
      {...props}
    />
  );
}
