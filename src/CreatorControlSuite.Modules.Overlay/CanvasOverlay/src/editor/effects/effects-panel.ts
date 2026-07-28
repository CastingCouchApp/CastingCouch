import type { EffectField, EffectInstance, LayoutItem } from "../../shared/types";
import { EFFECT_STRATEGIES } from "../../shared/effects/registry";
import { effectTargets, resolveEffectTarget } from "../../shared/effects/apply";
import type { EditorContext } from "../props/context";
import { propSection } from "../sections/prop-section";
import { colorProp } from "../controls/color-prop";
import { numProp } from "../controls/num-prop";
import { boolProp } from "../controls/bool-prop";
import { textProp } from "../controls/text-prop";
import { selectProp } from "../controls/select-prop";

function uid(): string {
  return "fx-" + Math.random().toString(36).slice(2, 9);
}

function ensureSettings(effect: EffectInstance): Record<string, unknown> {
  if (!effect.settings) effect.settings = {};
  return effect.settings;
}

function renderField(
  field: EffectField,
  effect: EffectInstance,
  effectIndex: number,
  item: LayoutItem,
  ctx: EditorContext,
  body: HTMLElement
): void {
  const settings = ensureSettings(effect);
  const proxyItem: LayoutItem = {
    ...item,
    props: settings
  };
  const fieldCtx: EditorContext = {
    ...ctx,
    commitProp: (from, apply) => {
      return ctx.commitProp(item, (live) => {
        live.effects = live.effects || [];
        const target = live.effects[effectIndex];
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

const TARGET_LABELS: Record<string, string> = {
  box: "Box",
  content: "Inhalt"
};

export function renderEffectsPanel(container: HTMLElement, item: LayoutItem, ctx: EditorContext): void {
  const { root, body } = propSection("effects", "Effekte", false);
  item.effects = item.effects || [];

  const list = document.createElement("div");
  list.className = "ccs-effects-list";

  function renderList(): void {
    list.innerHTML = "";
    const live = ctx.liveItem(item) || item;
    live.effects = live.effects || [];
    for (let i = 0; i < live.effects.length; i++) {
      const effect = live.effects[i];
      const strategy = EFFECT_STRATEGIES[effect.type];
      const card = document.createElement("div");
      card.className = "ccs-effect-instance";

      const header = document.createElement("label");
      header.className = "ccs-feature-header";
      const enabled = document.createElement("input");
      enabled.type = "checkbox";
      enabled.checked = effect.enabled !== false;
      enabled.addEventListener("change", () => {
        ctx.commitProp(item, (next) => {
          if (!next.effects || !next.effects[i]) return;
          next.effects[i].enabled = enabled.checked;
        });
        settingsBody.classList.toggle("ccs-feature-disabled", !enabled.checked);
      });
      const title = document.createElement("span");
      title.textContent = strategy?.label || effect.type;
      header.appendChild(enabled);
      header.appendChild(title);

      const typeSelect = document.createElement("select");
      for (const t of window.CcsCanvas.listEffectTypes()) {
        const opt = document.createElement("option");
        opt.value = t;
        opt.textContent = EFFECT_STRATEGIES[t]?.label || t;
        if (t === effect.type) opt.selected = true;
        typeSelect.appendChild(opt);
      }
      typeSelect.addEventListener("change", () => {
        ctx.commitProp(item, (next) => {
          if (!next.effects || !next.effects[i]) return;
          const strat = EFFECT_STRATEGIES[typeSelect.value];
          next.effects[i].type = typeSelect.value;
          next.effects[i].settings = { ...(strat?.defaults || {}) };
          next.effects[i].target = resolveEffectTarget(next.effects[i], strat);
        });
        renderList();
      });

      const allowedTargets = effectTargets(strategy);
      const resolvedTarget = resolveEffectTarget(effect, strategy);
      let targetSelect: HTMLSelectElement | null = null;
      if (allowedTargets.length > 1) {
        targetSelect = document.createElement("select");
        targetSelect.title = "Effekt-Ziel";
        for (const value of allowedTargets) {
          const opt = document.createElement("option");
          opt.value = value;
          opt.textContent = TARGET_LABELS[value] || value;
          if (resolvedTarget === value) opt.selected = true;
          targetSelect.appendChild(opt);
        }
        targetSelect.addEventListener("change", () => {
          ctx.commitProp(item, (next) => {
            if (!next.effects || !next.effects[i] || !targetSelect) return;
            next.effects[i].target = targetSelect.value === "content" ? "content" : "box";
          });
        });
      }

      const remove = document.createElement("button");
      remove.type = "button";
      remove.textContent = "×";
      remove.title = "Entfernen";
      remove.addEventListener("click", () => {
        ctx.commitProp(item, (next) => {
          next.effects = (next.effects || []).filter((_, idx) => idx !== i);
        });
        renderList();
      });

      const row = document.createElement("div");
      row.className = "ccs-effect-row";
      row.appendChild(header);
      row.appendChild(typeSelect);
      if (targetSelect) row.appendChild(targetSelect);
      row.appendChild(remove);
      card.appendChild(row);

      const settingsBody = document.createElement("div");
      settingsBody.className = "ccs-feature-body";
      if (effect.enabled === false) settingsBody.classList.add("ccs-feature-disabled");
      for (const field of strategy?.fields || []) {
        renderField(field, effect, i, live, ctx, settingsBody);
      }
      card.appendChild(settingsBody);
      list.appendChild(card);
    }
  }

  const addBtn = document.createElement("button");
  addBtn.type = "button";
  addBtn.textContent = "Effekt hinzufügen";
  addBtn.className = "ccs-palette-item";
  addBtn.addEventListener("click", () => {
    const type = window.CcsCanvas.listEffectTypes()[0] || "glow";
    const strat = EFFECT_STRATEGIES[type];
    ctx.commitProp(item, (live) => {
      live.effects = live.effects || [];
      live.effects.push({
        id: uid(),
        type,
        enabled: true,
        target: "box",
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
