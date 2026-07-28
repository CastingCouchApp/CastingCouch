import type { LayoutItem } from "../../shared/types";
import type { EditorContext } from "../props/context";

type AssetCatalogItem = {
  id: string;
  name: string;
  url: string;
  contentType?: string;
};

let modalEl: HTMLElement | null = null;

async function fetchAssets(): Promise<AssetCatalogItem[]> {
  try {
    const data = (await window.CcsCanvas.fetchJson("/assets")) as { assets?: AssetCatalogItem[] };
    return Array.isArray(data?.assets) ? data.assets : [];
  } catch {
    return [];
  }
}

async function uploadAsset(file: File): Promise<AssetCatalogItem | null> {
  const form = new FormData();
  form.append("file", file, file.name);
  const res = await fetch("/assets", { method: "POST", body: form });
  if (!res.ok) return null;
  return (await res.json()) as AssetCatalogItem;
}

async function deleteAsset(id: string): Promise<boolean> {
  const res = await fetch("/assets/" + encodeURIComponent(id), { method: "DELETE" });
  return res.ok;
}

function closeModal(): void {
  if (modalEl) {
    modalEl.remove();
    modalEl = null;
  }
}

function openAssetLibrary(onSelect: (url: string) => void): void {
  closeModal();
  const overlay = document.createElement("div");
  overlay.className = "ccs-asset-modal-overlay";
  const panel = document.createElement("div");
  panel.className = "ccs-asset-modal";
  panel.innerHTML =
    `<div class="ccs-asset-modal-head">` +
    `<strong>Asset-Bibliothek</strong>` +
    `<button type="button" class="ccs-asset-modal-close" aria-label="Schließen">×</button>` +
    `</div>` +
    `<div class="ccs-asset-modal-toolbar">` +
    `<button type="button" class="ccs-asset-upload-btn">Hochladen…</button>` +
    `<input type="file" accept="image/png,image/jpeg,image/webp,image/gif,image/bmp,image/svg+xml,.png,.jpg,.jpeg,.webp,.gif,.bmp,.svg" hidden />` +
    `</div>` +
    `<div class="ccs-asset-grid"></div>` +
    `<div class="ccs-asset-modal-empty" hidden>Keine Assets. Lade ein Bild hoch.</div>`;

  overlay.appendChild(panel);
  document.body.appendChild(overlay);
  modalEl = overlay;

  const grid = panel.querySelector(".ccs-asset-grid") as HTMLElement;
  const empty = panel.querySelector(".ccs-asset-modal-empty") as HTMLElement;
  const fileInput = panel.querySelector('input[type="file"]') as HTMLInputElement;

  panel.querySelector(".ccs-asset-modal-close")!.addEventListener("click", closeModal);
  overlay.addEventListener("click", (e) => {
    if (e.target === overlay) closeModal();
  });
  panel.querySelector(".ccs-asset-upload-btn")!.addEventListener("click", () => fileInput.click());
  fileInput.addEventListener("change", async () => {
    const file = fileInput.files?.[0];
    fileInput.value = "";
    if (!file) return;
    const uploaded = await uploadAsset(file);
    if (uploaded?.url) {
      onSelect(uploaded.url);
      closeModal();
      return;
    }
    await renderGrid();
  });

  async function renderGrid(): Promise<void> {
    const assets = await fetchAssets();
    grid.innerHTML = "";
    empty.hidden = assets.length > 0;
    for (const asset of assets) {
      const card = document.createElement("div");
      card.className = "ccs-asset-card";
      const img = document.createElement("img");
      img.src = asset.url;
      img.alt = asset.name || asset.id;
      img.loading = "lazy";
      const name = document.createElement("div");
      name.className = "ccs-asset-card-name";
      name.textContent = asset.name || asset.id;
      const actions = document.createElement("div");
      actions.className = "ccs-asset-card-actions";
      const useBtn = document.createElement("button");
      useBtn.type = "button";
      useBtn.textContent = "Auswählen";
      useBtn.addEventListener("click", () => {
        onSelect(asset.url);
        closeModal();
      });
      const delBtn = document.createElement("button");
      delBtn.type = "button";
      delBtn.textContent = "Löschen";
      delBtn.addEventListener("click", async () => {
        if (!(await deleteAsset(asset.id))) return;
        await renderGrid();
      });
      actions.appendChild(useBtn);
      actions.appendChild(delBtn);
      card.appendChild(img);
      card.appendChild(name);
      card.appendChild(actions);
      grid.appendChild(card);
    }
  }

  void renderGrid();
}

export function imageProp(
  key: string,
  label: string,
  item: LayoutItem,
  ctx: EditorContext,
  fallback: string
): HTMLElement {
  const wrap = document.createElement("div");
  wrap.className = "ccs-prop-row ccs-image-prop";

  const title = document.createElement("span");
  title.className = "ccs-prop-row-label";
  title.textContent = label;

  const row = document.createElement("div");
  row.className = "ccs-image-prop-controls";

  const input = document.createElement("input");
  input.type = "text";
  input.className = "ccs-prop-row-control";
  input.placeholder = "https://… oder /assets/…";
  const props = item.props as Record<string, unknown> | undefined;
  input.value = (props && props[key] != null ? String(props[key]) : fallback) || fallback;
  input.addEventListener("change", () => {
    ctx.commitProp(item, (live) => {
      live.props[key] = input.value;
    });
  });

  const libBtn = document.createElement("button");
  libBtn.type = "button";
  libBtn.className = "ccs-image-prop-library";
  libBtn.textContent = "Bibliothek…";
  libBtn.addEventListener("click", () => {
    openAssetLibrary((url) => {
      input.value = url;
      ctx.commitProp(item, (live) => {
        live.props[key] = url;
      });
    });
  });

  row.appendChild(input);
  row.appendChild(libBtn);
  wrap.appendChild(title);
  wrap.appendChild(row);
  return wrap;
}

/** Library button for custom list inputs (e.g. partner-roulette). */
export function attachImageLibraryButton(
  input: HTMLInputElement,
  onPicked: (url: string) => void
): HTMLButtonElement {
  const libBtn = document.createElement("button");
  libBtn.type = "button";
  libBtn.className = "ccs-image-prop-library";
  libBtn.textContent = "Bibliothek…";
  libBtn.addEventListener("click", () => {
    openAssetLibrary((url) => {
      input.value = url;
      onPicked(url);
    });
  });
  return libBtn;
}
