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
        variant === "primary" && "bg-brand text-on-accent hover:opacity-90",
        variant === "ghost" && "bg-input text-text hover:bg-nav-hover",
        variant === "danger" && "bg-danger text-white hover:opacity-90",
        "disabled:opacity-50",
        className,
      )}
      {...props}
    />
  );
}
