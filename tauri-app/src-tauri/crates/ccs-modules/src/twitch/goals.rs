use serde::{Deserialize, Serialize};
use serde_json::{json, Value};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GoalDraft {
    pub title: String,
    pub current: String,
    pub target: String,
    pub font_face: String,
    pub font_size: String,
    pub currency: String,
    pub reason: String,
}
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GoalsDraft {
    pub overlay_scene: String,
    pub follower: GoalDraft,
    pub subscriptions: GoalDraft,
    pub donation: GoalDraft,
}
pub fn goal_draft(settings: &Value) -> GoalsDraft {
    let goal = |key: &str, title: &str, target: u64| {
        let value = &settings["Twitch"][key];
        let text = |key: &str, default: &str| value[key].as_str().unwrap_or(default).to_owned();
        let number = |key: &str, default: u64| {
            value
                .get(key)
                .and_then(Value::as_f64)
                .map(|v| v.to_string())
                .unwrap_or_else(|| default.to_string())
        };
        GoalDraft {
            title: text("Title", title),
            current: number("Current", 0),
            target: number("Target", target),
            font_face: text("FontFace", "Segoe UI"),
            font_size: number("FontSize", 36),
            currency: text("Currency", "EUR"),
            reason: text("Reason", ""),
        }
    };
    GoalsDraft {
        overlay_scene: settings
            .pointer("/Obs/GoalOverlayScene")
            .and_then(Value::as_str)
            .unwrap_or("CCS Ziele & Overlay-Daten")
            .into(),
        follower: goal("FollowerGoal", "Follower-Ziel", 200),
        subscriptions: goal("SubGoal", "Sub-Ziel", 25),
        donation: goal("DonationGoal", "Donation-Ziel", 100),
    }
}
/// Patch only edited fields; the settings store merges against the original draft.
pub fn edit_goals(
    settings: &Value,
    draft: &GoalsDraft,
    followers: Option<u64>,
    subscriptions: Option<u64>,
) -> Result<Value, String> {
    if !settings.is_object() || !settings["Twitch"].is_object() || !settings["Obs"].is_object() {
        return Err("Ungültiger Einstellungsentwurf.".into());
    }
    let baseline = goal_draft(settings);
    let mut result = settings.clone();
    result["Obs"]["GoalOverlayScene"] = json!(if draft.overlay_scene.trim().is_empty() {
        "CCS Ziele & Overlay-Daten"
    } else {
        draft.overlay_scene.trim()
    });
    for (key, goal, previous, default_title, live) in [
        (
            "FollowerGoal",
            &draft.follower,
            &baseline.follower,
            "Follower-Ziel",
            followers,
        ),
        (
            "SubGoal",
            &draft.subscriptions,
            &baseline.subscriptions,
            "Sub-Ziel",
            subscriptions,
        ),
        (
            "DonationGoal",
            &draft.donation,
            &baseline.donation,
            "Donation-Ziel",
            None,
        ),
    ] {
        if !result["Twitch"][key].is_object() {
            result["Twitch"][key] = json!({});
        }
        let output = &mut result["Twitch"][key];
        output["Title"] = json!(if goal.title.trim().is_empty() {
            default_title
        } else {
            goal.title.trim()
        });
        let current = if let Some(live) = live {
            live as f64
        } else {
            number(&goal.current, &previous.current)?
        };
        if output["Current"].as_f64() != Some(current) {
            output["Current"] = json!(current);
        }
        let target = number(&goal.target, &previous.target)?;
        if output["Target"].as_f64() != Some(target) {
            output["Target"] = json!(target);
        }
        output["FontFace"] = json!(goal.font_face.trim());
        output["FontSize"] = json!(goal.font_size.trim().parse::<i32>().unwrap_or(36));
        if key == "DonationGoal" {
            output["Currency"] = json!(goal.currency.trim());
            output["Reason"] = json!(goal.reason.trim());
        }
    }
    Ok(result)
}
fn number(text: &str, fallback: &str) -> Result<f64, String> {
    let value = text
        .trim()
        .replace(',', ".")
        .parse::<f64>()
        .unwrap_or_else(|_| fallback.parse().unwrap_or(0.0));
    if value.is_finite() {
        Ok(value)
    } else {
        Err("Zielwerte müssen endliche Zahlen sein.".into())
    }
}
