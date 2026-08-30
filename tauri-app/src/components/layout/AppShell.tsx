import { Link, Outlet } from "@tanstack/react-router";
import {
  LayoutDashboard,
  Settings,
  Plug,
  Layers,
  Bell,
  Download,
  Info,
} from "lucide-react";
import { cn } from "../../lib/cn";

const nav = [
  { to: "/", label: "Dashboard", icon: LayoutDashboard },
  { to: "/services", label: "Dienste", icon: Plug },
  { to: "/overlay", label: "Overlay", icon: Layers },
  { to: "/alerts", label: "Alerts", icon: Bell },
  { to: "/settings", label: "Einstellungen", icon: Settings },
  { to: "/updates", label: "Updates", icon: Download },
  { to: "/about", label: "Über", icon: Info },
];

export function AppShell() {
  return (
    <div className="flex h-screen bg-ink text-zinc-100">
      <aside className="flex w-56 flex-col border-r border-white/10 bg-black/40">
        <div className="px-4 py-4 text-lg font-semibold tracking-tight">
          CastingCouch
        </div>
        <nav className="flex flex-1 flex-col gap-0.5 px-2">
          {nav.map((item) => {
            const Icon = item.icon;
            return (
              <Link
                key={item.to}
                to={item.to}
                className={cn(
                  "flex items-center gap-2 rounded-md px-3 py-2 text-sm text-zinc-300 hover:bg-white/10",
                )}
                activeProps={{ className: "bg-white/10 text-white" }}
              >
                <Icon className="h-4 w-4" />
                {item.label}
              </Link>
            );
          })}
        </nav>
      </aside>
      <main className="flex-1 overflow-auto p-6">
        <Outlet />
      </main>
    </div>
  );
}
