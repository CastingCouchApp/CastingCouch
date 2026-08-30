import { cn } from "../../lib/cn";
import type { ButtonHTMLAttributes } from "react";

type Props = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "primary" | "ghost" | "danger";
};

export function Button({ className, variant = "primary", ...props }: Props) {
  return (
    <button
      className={cn(
        "inline-flex items-center justify-center rounded-md px-3 py-1.5 text-sm font-medium transition",
        variant === "primary" && "bg-brand text-white hover:bg-brand/90",
        variant === "ghost" && "bg-white/5 hover:bg-white/10",
        variant === "danger" && "bg-red-600 text-white hover:bg-red-500",
        "disabled:opacity-50",
        className,
      )}
      {...props}
    />
  );
}
