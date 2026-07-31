import { describe, expect, it } from "vitest";
import { resolveGoalBarValues } from "../src/shared/widgets/goal-bar";
import type { LayoutItem } from "../src/shared/types";

function goal(kind: string, props: Record<string, unknown> = {}): LayoutItem {
  return { id: kind, kind: "widget", type: "goal-bar", x: 0, y: 0, w: 560, h: 88, z: 1, props: { kind, ...props } };
}

describe("goal bar Twitch defaults", () => {
  it("uses configured Twitch goal state when the item has no override", () => {
    const values = resolveGoalBarValues(goal("followers"), {
      twitch: { followers: 321, followerGoalState: { title: "Road to 500", target: 500 } }
    });

    expect(values).toEqual({ current: 321, target: 500, label: "Road to 500" });
  });

  it("keeps an explicit item override", () => {
    const values = resolveGoalBarValues(goal("subs", { label: "VIP", target: 75 }), {
      twitch: { subGoalState: { title: "Subs", current: 12, target: 50 } }
    });

    expect(values).toEqual({ current: 12, target: 75, label: "VIP" });
  });
});
