use ccs_modules::twitch::{edit_goals, goal_draft};
use serde_json::json;

#[test]
fn goal_editor_matches_csharp_normalization_and_preserves_unknown_settings() {
    let original = json!({"Obs":{"GoalOverlayScene":"Goals","Other":true},"Twitch":{"FollowerGoal":{"Title":"Old","Current":10,"Target":200,"Enabled":false,"Future":{"keep":true}},"SubGoal":{"Current":4,"Target":25},"DonationGoal":{"Current":12.5,"Target":100},"ClientId":"keep"}});
    let mut draft = goal_draft(&original);
    draft.overlay_scene = " ".into();
    draft.follower.title = " ".into();
    draft.follower.target = "250,5".into();
    draft.follower.font_face = " Inter ".into();
    draft.follower.font_size = "42".into();
    draft.donation.title = " Support ".into();
    draft.donation.current = "12,5".into();
    draft.donation.currency = " EUR ".into();
    draft.donation.reason = " Neues Mikrofon ".into();
    let edited = edit_goals(&original, &draft, Some(125), Some(0)).unwrap();
    assert_eq!(
        edited["Obs"]["GoalOverlayScene"],
        "CCS Ziele & Overlay-Daten"
    );
    assert_eq!(edited["Twitch"]["FollowerGoal"]["Title"], "Follower-Ziel");
    assert_eq!(edited["Twitch"]["FollowerGoal"]["Target"], 250.5);
    assert_eq!(edited["Twitch"]["FollowerGoal"]["Current"], 125.0);
    assert_eq!(edited["Twitch"]["FollowerGoal"]["FontFace"], "Inter");
    assert_eq!(edited["Twitch"]["FollowerGoal"]["FontSize"], 42);
    assert_eq!(edited["Twitch"]["FollowerGoal"]["Enabled"], false);
    assert_eq!(
        edited["Twitch"]["FollowerGoal"]["Future"],
        json!({"keep":true})
    );
    assert_eq!(edited["Twitch"]["SubGoal"]["Current"], 0.0);
    assert_eq!(edited["Twitch"]["DonationGoal"]["Current"], 12.5);
    assert_eq!(edited["Twitch"]["DonationGoal"]["Reason"], "Neues Mikrofon");
    assert_eq!(edited["Twitch"]["ClientId"], "keep");
}

#[test]
fn goal_defaults_and_invalid_numbers_follow_csharp_fallback_without_serializing_nan() {
    let original = json!({"Obs":{},"Twitch":{}});
    let mut draft = goal_draft(&original);
    assert_eq!(draft.follower.target, "200");
    assert_eq!(draft.subscriptions.target, "25");
    assert_eq!(draft.donation.currency, "EUR");
    draft.follower.target = "invalid".into();
    draft.follower.font_size = "invalid".into();
    let edited = edit_goals(&original, &draft, None, None).unwrap();
    assert_eq!(edited["Twitch"]["FollowerGoal"]["Target"], 200.0);
    assert_eq!(edited["Twitch"]["FollowerGoal"]["FontSize"], 36);
    for value in ["NaN", "Infinity", "1e999"] {
        draft.donation.target = value.into();
        assert!(edit_goals(&original, &draft, None, None).is_err());
    }
}

#[test]
fn unchanged_goal_numbers_keep_existing_json_representation() {
    let original = json!({"Obs":{},"Twitch":{"FollowerGoal":{"Current":1,"Target":200},"SubGoal":{"Target":25},"DonationGoal":{"Target":100}}});
    let edited = edit_goals(&original, &goal_draft(&original), None, None).unwrap();
    assert_eq!(
        edited["Twitch"]["FollowerGoal"]["Target"],
        original["Twitch"]["FollowerGoal"]["Target"]
    );
    assert_eq!(
        edited["Twitch"]["FollowerGoal"]["Current"],
        original["Twitch"]["FollowerGoal"]["Current"]
    );
}
