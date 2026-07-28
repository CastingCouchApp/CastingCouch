import type { AnimationField, AnimationInstance, LayoutItem } from "../../shared/types";
import { ANIMATION_STRATEGIES } from "../../shared/animations/registry";
import type { EditorContext } from "../props/context";
import { propSection } from "../sections/prop-section";
import { colorProp } from "../controls/color-prop";
import { numProp } from "../controls/num-prop";
import { boolProp } from "../controls/bool-prop";
import { textProp } from "../controls/text-prop";
import { selectProp } from "../controls/select-prop";

function uid(): string {
  return "an-" + Math.random().toString(36).slice(2, 9);
}

function ensureSettings(animation: AnimationInstance): Record<string, unknown> {
  if (!animation.settings) animation.settings = {};
  return animation.settings;
}

function renderField(
  field: AnimationField,
  animation: AnimationInstance,
  animationIndex: number,
  item: LayoutItem,
  ctx: EditorContext,
  body: HTMLElement
): void {
  const settings = ensureSettings(animation);
  const proxyItem: LayoutItem = {
    ...item,
    props: settings
  };
  const fieldCtx: EditorContext = {
    ...ctx,
    commitProp: (from, apply) => {
      return ctx.commitProp(item, (live) => {
        live.animations = live.animations || [];
        const target = live.animations[animationIndex];
        if (!target) return;
        target.settings = target.settings || {};
        const fake: LayoutItem = { ...live, props: target.settings };
        apply(fake);
        target.settings = fake.props;
      });
    }
  };

  if (field.kind === "color") {
    body.appendChild(colorProp(field.key, field.label, proxyItem, fieldCtx, String(field.fallback ?? "#ffffff")));
  } else if (field.kind === "number") {
    body.appendChild(numProp(field.key, field.label, proxyItem, fieldCtx, Number(field.fallback ?? 0), {
      step: field.step,
      min: field.min,
      max: field.max
    }));
  } else if (field.kind === "bool") {
    body.appendChild(boolProp(field.key, field.label, proxyItem, fieldCtx));
  } else if (field.kind === "select") {
    body.appendChild(selectProp(
      field.key,
      field.label,
      proxyItem,
      fieldCtx,
      field.options || [],
      String(field.fallback ?? "")
    ));
  } else {
    body.appendChild(textProp(field.key, field.label, proxyItem, fieldCtx, String(field.fallback ?? "")));
  }
}

export function renderAnimationsPanel(container: HTMLElement, item: LayoutItem, ctx: EditorContext): void {
  const { root, body } = propSection("animations", "Animationen", false);
  item.animations = item.animations || [];

  const list = document.createElement("div");
  list.className = "ccs-animations-list";

  function renderList(): void {
    list.innerHTML = "";
    const live = ctx.liveItem(item) || item;
    live.animations = live.animations || [];
    for (let i = 0; i < live.animations.length; i++) {
      const animation = live.animations[i];
      const strategy = ANIMATION_STRATEGIES[animation.type];
      const card = document.createElement("div");
      card.className = "ccs-animation-instance";

      const header = document.createElement("label");
      header.className = "ccs-feature-header";
      const enabled = document.createElement("input");
      enabled.type = "checkbox";
      enabled.checked = animation.enabled !== false;
      enabled.addEventListener("change", () => {
        ctx.commitProp(item, (next) => {
          if (!next.animations || !next.animations[i]) return;
          next.animations[i].enabled = enabled.checked;
        });
        settingsBody.classList.toggle("ccs-feature-disabled", !enabled.checked);
      });
      const title = document.createElement("span");
      title.textContent = strategy?.label || animation.type;
      header.appendChild(enabled);
      header.appendChild(title);

      const typeSelect = document.createElement("select");
      for (const t of window.CcsCanvas.listAnimationTypes()) {
        const opt = document.createElement("option");
        opt.value = t;
        opt.textContent = ANIMATION_STRATEGIES[t]?.label || t;
        if (t === animation.type) opt.selected = true;
        typeSelect.appendChild(opt);
      }
      typeSelect.addEventListener("change", () => {
        ctx.commitProp(item, (next) => {
          if (!next.animations || !next.animations[i]) return;
          const strat = ANIMATION_STRATEGIES[typeSelect.value];
          next.animations[i].type = typeSelect.value;
          next.animations[i].settings = { ...(strat?.defaults || {}) };
        });
        renderList();
      });

      const remove = document.createElement("button");
      remove.type = "button";
      remove.textContent = "×";
      remove.title = "Entfernen";
      remove.addEventListener("click", () => {
        ctx.commitProp(item, (next) => {
          next.animations = (next.animations || []).filter((_, idx) => idx !== i);
        });
        renderList();
      });

      const row = document.createElement("div");
      row.className = "ccs-effect-row";
      row.appendChild(header);
      row.appendChild(typeSelect);
      row.appendChild(remove);
      card.appendChild(row);

      const settingsBody = document.createElement("div");
      settingsBody.className = "ccs-feature-body";
      if (animation.enabled === false) settingsBody.classList.add("ccs-feature-disabled");
      for (const field of strategy?.fields || []) {
        renderField(field, animation, i, live, ctx, settingsBody);
      }
      card.appendChild(settingsBody);
      list.appendChild(card);
    }
  }

  const addBtn = document.createElement("button");
  addBtn.type = "button";
  addBtn.textContent = "Animation hinzufügen";
  addBtn.className = "ccs-palette-item";
  addBtn.addEventListener("click", () => {
    const type = window.CcsCanvas.listAnimationTypes()[0] || "fade";
    const strat = ANIMATION_STRATEGIES[type];
    ctx.commitProp(item, (live) => {
      live.animations = live.animations || [];
      live.animations.push({
        id: uid(),
        type,
        enabled: true,
        settings: { ...(strat?.defaults || {}) }
      });
    });
    renderList();
  });

  renderList();
  body.appendChild(list);
  body.appendChild(addBtn);
  container.appendChild(root);
}
