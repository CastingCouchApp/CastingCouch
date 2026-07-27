import type { CreateRuntime, LayoutItem } from "../../shared/types";
import { runEditorCommand, type EditorCommand } from "./commands";

interface MenuEntry {
  command?: EditorCommand;
  label: string;
  separator?: boolean;
  disabled?: (item: LayoutItem) => boolean;
}

const ENTRIES: MenuEntry[] = [
  { command: "duplicate", label: "Duplizieren" },
  { separator: true, label: "" },
  {
    command: "toggleLock",
    label: "Sperren",
    disabled: (item) => !!item.locked
  },
  {
    command: "toggleLock",
    label: "Entsperren",
    disabled: (item) => !item.locked
  },
  { separator: true, label: "" },
  { command: "bringFront", label: "Ganz nach oben" },
  { command: "layerUp", label: "Ebene rauf" },
  { command: "layerDown", label: "Ebene runter" },
  { command: "sendBack", label: "Ganz nach unten" },
  { separator: true, label: "" },
  { command: "delete", label: "Löschen", disabled: (item) => !!item.locked }
];

export function setupContextMenu(
  stage: HTMLElement,
  runtime: CreateRuntime,
  scheduleSave: () => void,
  onAfterCommand?: () => void
): void {
  let menu: HTMLDivElement | null = null;

  function close(): void {
    if (menu) {
      menu.remove();
      menu = null;
    }
  }

  function open(clientX: number, clientY: number, item: LayoutItem): void {
    close();
    menu = document.createElement("div");
    menu.className = "ccs-context-menu";
    menu.style.left = clientX + "px";
    menu.style.top = clientY + "px";

    for (const entry of ENTRIES) {
      if (entry.separator) {
        const sep = document.createElement("div");
        sep.className = "ccs-context-menu-sep";
        menu.appendChild(sep);
        continue;
      }
      if (entry.disabled && entry.disabled(item)) {
        continue;
      }
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "ccs-context-menu-item";
      btn.textContent = entry.label;
      btn.addEventListener("click", (e) => {
        e.stopPropagation();
        if (entry.command) {
          runEditorCommand(entry.command, runtime, scheduleSave);
          onAfterCommand?.();
        }
        close();
      });
      menu.appendChild(btn);
    }

    document.body.appendChild(menu);
  }

  stage.addEventListener("contextmenu", (e) => {
    e.preventDefault();
    const wrapper = (e.target as HTMLElement).closest(".ccs-item") as HTMLElement | null;
    if (!wrapper) {
      close();
      runtime.select(null);
      return;
    }
    const id = wrapper.dataset.id;
    if (!id) return;
    runtime.select(id);
    const item = (runtime.getLayout().items || []).find((i) => i.id === id);
    if (!item) return;
    open(e.clientX, e.clientY, item);
  });

  document.addEventListener("pointerdown", (e) => {
    if (!menu) return;
    if (menu.contains(e.target as Node)) return;
    close();
  });

  window.addEventListener("blur", close);
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") close();
  });
}
