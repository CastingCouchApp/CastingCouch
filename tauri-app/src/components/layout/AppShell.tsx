import { useEffect } from "react";
import { Link, Outlet } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import {
  LayoutDashboard,
  Settings,
  Plug,
  Layers,
  Bell,
  Download,
  Info,
  Music,
  ListTodo,
} from "lucide-react";
import { cn } from "../../lib/cn";
import { applyThemeId, type AppSettings } from "../../lib/app-settings";
import { queryKeys, tauriInvoke } from "../../lib/api";

const nav = [
  { to: "/", label: "Dashboard", icon: LayoutDashboard },
  { to: "/services", label: "Dienste", icon: Plug },
  { to: "/music", label: "Musik", icon: Music },
  { to: "/workflow", label: "Workflow", icon: ListTodo },
  { to: "/overlay", label: "Overlay", icon: Layers },
  { to: "/alerts", label: "Alerts", icon: Bell },
  { to: "/settings", label: "Einstellungen", icon: Settings },
  { to: "/updates", label: "Updates", icon: Download },
  { to: "/about", label: "Über", icon: Info },
];

export function AppShell() {
  const settings = useQuery({
    queryKey: queryKeys.settings,
    queryFn: () => tauriInvoke<AppSettings>("get_settings"),
  });

  useEffect(() => {
    if (settings.data?.General.ThemeId) {
      applyThemeId(settings.data.General.ThemeId);
    }
  }, [settings.data?.General.ThemeId]);

  return (
    <div className="flex h-screen bg-window font-app text-text">
      <aside className="flex w-56 flex-col border-r border-border bg-sidebar">
        <div className="px-4 py-4 text-lg font-semibold tracking-tight text-text">
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
                  "flex items-center gap-2 rounded-md px-3 py-2 text-sm text-text-secondary hover:bg-nav-hover hover:text-text",
                )}
                activeProps={{ className: "bg-nav-active text-nav-fg" }}
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
