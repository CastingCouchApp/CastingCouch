use super::{TwitchClient, TwitchHelixClient};
use crate::{ModuleError, ModuleResult};
use serde::Deserialize;
use serde_json::{json, Value};

#[derive(Debug, Deserialize)]
#[serde(
    tag = "action",
    rename_all = "snake_case",
    rename_all_fields = "camelCase"
)]
pub enum TwitchAction {
    Channel {
        title: String,
        category_id: String,
    },
    SendChat {
        message: String,
    },
    Ban {
        id: String,
        duration: Option<u32>,
        reason: String,
    },
    Unban {
        id: String,
    },
    DeleteChat {
        message_id: Option<String>,
    },
    Raid {
        id: String,
    },
    CancelRaid,
    CreateReward {
        title: String,
        cost: u32,
        prompt: String,
        is_enabled: Option<bool>,
        is_user_input_required: Option<bool>,
        background_color: Option<String>,
    },
    UpdateReward {
        id: String,
        title: Option<String>,
        cost: Option<u32>,
        prompt: Option<String>,
        is_enabled: Option<bool>,
        is_paused: Option<bool>,
        is_user_input_required: Option<bool>,
        background_color: Option<String>,
    },
    DeleteReward {
        id: String,
    },
    UpdateRedemption {
        reward_id: String,
        id: String,
        status: String,
    },
    CreatePoll {
        title: String,
        choices: Vec<String>,
        duration: u32,
    },
    EndPoll {
        id: String,
        status: String,
    },
    CreatePrediction {
        title: String,
        outcomes: Vec<String>,
        window: u32,
    },
    EndPrediction {
        id: String,
        status: String,
        winning_outcome_id: Option<String>,
    },
}
#[derive(Debug, Deserialize)]
#[serde(
    tag = "query",
    rename_all = "snake_case",
    rename_all_fields = "camelCase"
)]
pub enum TwitchQuery {
    Channel,
    Stream,
    Followers,
    Subscriptions,
    Chatters,
    FollowedChannels,
    FollowedStreams,
    Rewards,
    Polls,
    Predictions,
    SearchCategories {
        text: String,
    },
    SearchChannels {
        text: String,
    },
    Redemptions {
        reward_id: String,
        status: Option<String>,
    },
}
struct Request {
    method: reqwest::Method,
    path: &'static str,
    params: Vec<(String, String)>,
    body: Option<Value>,
}
impl Request {
    fn new(method: reqwest::Method, path: &'static str) -> Self {
        Self {
            method,
            path,
            params: vec![],
            body: None,
        }
    }
    fn param(mut self, k: &str, v: impl ToString) -> Self {
        self.params.push((k.into(), v.to_string()));
        self
    }
    fn body(mut self, v: Value) -> Self {
        self.body = Some(v);
        self
    }
}
fn invalid(message: &str) -> ModuleError {
    ModuleError::Message(message.into())
}
fn checked_text<'a>(value: &'a str, max: usize, name: &str) -> ModuleResult<&'a str> {
    let value = value.trim();
    if value.is_empty() || value.chars().count() > max {
        return Err(invalid(&format!("{name} muss 1–{max} Zeichen enthalten")));
    }
    Ok(value)
}
fn checked_prompt(value: &str) -> ModuleResult<&str> {
    if value.trim().chars().count() > 200 {
        return Err(invalid(
            "Reward-Beschreibung darf höchstens 200 Zeichen enthalten",
        ));
    }
    Ok(value.trim())
}
fn checked_color(value: &str) -> ModuleResult<&str> {
    if value.len() != 7
        || !value.starts_with('#')
        || !value[1..].bytes().all(|b| b.is_ascii_hexdigit())
    {
        return Err(invalid("Reward-Farbe muss #RRGGBB sein"));
    }
    Ok(value)
}
fn checked_choices(choices: &[String], max: usize) -> ModuleResult<Vec<Value>> {
    if !(2..=max).contains(&choices.len()) {
        return Err(invalid(&format!("Es sind 2–{max} Antworten erforderlich")));
    }
    choices
        .iter()
        .map(|value| Ok(json!({"title":checked_text(value,25,"Antwort")?})))
        .collect()
}
fn checked_status(value: &str, allowed: &[&str]) -> ModuleResult<()> {
    if !allowed.contains(&value) {
        return Err(invalid("Ungültiger Twitch-Status"));
    }
    Ok(())
}
impl TwitchAction {
    fn request(&self, broadcaster: &str, user: &str) -> ModuleResult<Request> {
        use reqwest::Method;
        Ok(match self {
            Self::Channel{title,category_id}=>Request::new(Method::PATCH,"channels").param("broadcaster_id",broadcaster).body(json!({"title":title,"game_id":category_id})),
            Self::SendChat{message}=>{if message.trim().is_empty()||message.chars().count()>500{return Err(ModuleError::Message("Chatnachricht muss 1–500 Zeichen enthalten".into()));}Request::new(Method::POST,"chat/messages").body(json!({"broadcaster_id":broadcaster,"sender_id":user,"message":message}))},
            Self::Ban{id,duration,reason}=>{let mut data=json!({"user_id":id,"reason":reason});if let Some(duration)=duration{data["duration"]=json!(duration);}Request::new(Method::POST,"moderation/bans").param("broadcaster_id",broadcaster).param("moderator_id",user).body(json!({"data":data}))},
            Self::Unban{id}=>Request::new(Method::DELETE,"moderation/bans").param("broadcaster_id",broadcaster).param("moderator_id",user).param("user_id",id),
            Self::DeleteChat{message_id}=>{let request=Request::new(Method::DELETE,"moderation/chat").param("broadcaster_id",broadcaster).param("moderator_id",user);if let Some(id)=message_id {request.param("message_id",id)}else{request}},
            Self::Raid{id}=>Request::new(Method::POST,"raids").param("from_broadcaster_id",broadcaster).param("to_broadcaster_id",id),
            Self::CancelRaid=>Request::new(Method::DELETE,"raids").param("broadcaster_id",broadcaster),
            Self::CreateReward {title,cost,prompt,is_enabled,is_user_input_required,background_color} => {
                let mut data=json!({"title":checked_text(title,45,"Reward-Titel")?,"cost":cost.max(&1),"prompt":checked_prompt(prompt)?,"is_enabled":is_enabled.unwrap_or(true)});
                if let Some(required)=is_user_input_required {data["is_user_input_required"]=json!(required);}
                if let Some(color)=background_color {data["background_color"]=json!(checked_color(color)?);}
                Request::new(Method::POST,"channel_points/custom_rewards").param("broadcaster_id",broadcaster).body(data)
            },
            Self::UpdateReward {id,title,cost,prompt,is_enabled,is_paused,is_user_input_required,background_color} => {
                let mut data=json!({});
                if let Some(title)=title {data["title"]=json!(checked_text(title,45,"Reward-Titel")?);}
                if let Some(cost)=cost {if *cost==0 {return Err(invalid("Reward-Kosten müssen mindestens 1 betragen"));} data["cost"]=json!(cost);}
                if let Some(prompt)=prompt {data["prompt"]=json!(checked_prompt(prompt)?);}
                for (key,value) in [("is_enabled",is_enabled),("is_paused",is_paused),("is_user_input_required",is_user_input_required)] {if let Some(value)=value {data[key]=json!(value);}}
                if let Some(color)=background_color {data["background_color"]=json!(checked_color(color)?);}
                if data.as_object().unwrap().is_empty() {return Err(invalid("Keine Reward-Änderung angegeben"));}
                Request::new(Method::PATCH,"channel_points/custom_rewards").param("broadcaster_id",broadcaster).param("id",checked_text(id,200,"Reward-ID")?).body(data)
            },
            Self::DeleteReward {id} => Request::new(Method::DELETE,"channel_points/custom_rewards").param("broadcaster_id",broadcaster).param("id",checked_text(id,200,"Reward-ID")?),
            Self::UpdateRedemption{reward_id,id,status}=>{checked_status(status,&["FULFILLED","CANCELED"])?;Request::new(Method::PATCH,"channel_points/custom_rewards/redemptions").param("broadcaster_id",broadcaster).param("reward_id",checked_text(reward_id,200,"Reward-ID")?).param("id",checked_text(id,200,"Einlösungs-ID")?).body(json!({"status":status}))},
            Self::CreatePoll{title,choices,duration}=>Request::new(Method::POST,"polls").body(json!({"broadcaster_id":broadcaster,"title":checked_text(title,60,"Umfragetitel")?,"choices":checked_choices(choices,5)?,"duration":duration.clamp(&15,&1800)})),
            Self::EndPoll{id,status}=>{checked_status(status,&["TERMINATED","ARCHIVED"])?;Request::new(Method::PATCH,"polls").body(json!({"broadcaster_id":broadcaster,"id":checked_text(id,200,"Umfrage-ID")?,"status":status}))},
            Self::CreatePrediction{title,outcomes,window}=>Request::new(Method::POST,"predictions").body(json!({"broadcaster_id":broadcaster,"title":checked_text(title,45,"Vorhersagetitel")?,"outcomes":checked_choices(outcomes,10)?,"prediction_window":window.clamp(&30,&1800)})),
            Self::EndPrediction{id,status,winning_outcome_id}=>{checked_status(status,&["LOCKED","RESOLVED","CANCELED"])?;let mut data=json!({"broadcaster_id":broadcaster,"id":checked_text(id,200,"Vorhersage-ID")?,"status":status});if status=="RESOLVED" {let winner=winning_outcome_id.as_deref().ok_or_else(||invalid("Gewinnendes Ergebnis fehlt"))?;data["winning_outcome_id"]=json!(checked_text(winner,200,"Ergebnis-ID")?);}Request::new(Method::PATCH,"predictions").body(data)},
        })
    }
}
impl TwitchQuery {
    fn request(&self, broadcaster: &str, user: &str) -> Request {
        use reqwest::Method;
        match self {
            Self::Channel => {
                Request::new(Method::GET, "channels").param("broadcaster_id", broadcaster)
            }
            Self::Stream => Request::new(Method::GET, "streams").param("user_id", broadcaster),
            Self::Followers => {
                Request::new(Method::GET, "channels/followers").param("broadcaster_id", broadcaster)
            }
            Self::Subscriptions => {
                Request::new(Method::GET, "subscriptions").param("broadcaster_id", broadcaster)
            }
            Self::Chatters => Request::new(Method::GET, "chat/chatters")
                .param("broadcaster_id", broadcaster)
                .param("moderator_id", user),
            Self::FollowedChannels => {
                Request::new(Method::GET, "channels/followed").param("user_id", user)
            }
            Self::FollowedStreams => {
                Request::new(Method::GET, "streams/followed").param("user_id", user)
            }
            Self::Rewards => Request::new(Method::GET, "channel_points/custom_rewards")
                .param("broadcaster_id", broadcaster),
            Self::Polls => Request::new(Method::GET, "polls").param("broadcaster_id", broadcaster),
            Self::Predictions => {
                Request::new(Method::GET, "predictions").param("broadcaster_id", broadcaster)
            }
            Self::SearchCategories { text } => {
                Request::new(Method::GET, "search/categories").param("query", text)
            }
            Self::SearchChannels { text } => {
                Request::new(Method::GET, "search/channels").param("query", text)
            }
            Self::Redemptions { reward_id, status } => {
                Request::new(Method::GET, "channel_points/custom_rewards/redemptions")
                    .param("broadcaster_id", broadcaster)
                    .param("reward_id", reward_id)
                    .param("status", status.as_deref().unwrap_or("UNFULFILLED"))
                    .param("sort", "OLDEST")
            }
        }
    }
}
impl TwitchHelixClient {
    async fn perform(&self, request: Request, after: Option<String>) -> ModuleResult<Value> {
        let mut call = self
            .http
            .request(
                request.method,
                format!("{}{}", self.helix_base, request.path),
            )
            .bearer_auth(&self.access_token)
            .header("Client-Id", &self.client_id)
            .query(&request.params);
        if let Some(after) = after {
            call = call.query(&[("after", after)]);
        }
        if let Some(body) = request.body {
            call = call.json(&body);
        }
        let response = call.send().await?;
        let status = response.status();
        let body = response.text().await?;
        if !status.is_success() {
            return Err(ModuleError::Message(format!(
                "Twitch API {}: {}",
                status.as_u16(),
                super::helix::parse_helix_error(&body)
            )));
        }
        if body.is_empty() {
            return Ok(Value::Null);
        }
        let value: Value =
            serde_json::from_str(&body).map_err(|e| ModuleError::Message(e.to_string()))?;
        if value.pointer("/data/0/is_sent") == Some(&json!(false)) {
            return Err(ModuleError::Message(format!(
                "Twitch hat die Nachricht abgelehnt: {}",
                value
                    .pointer("/data/0/drop_reason/message")
                    .and_then(Value::as_str)
                    .unwrap_or("unbekannter Grund")
            )));
        }
        Ok(value)
    }
}
impl TwitchClient {
    pub(super) async fn perform_moderation(
        &self,
        client_id: &str,
        channel: &str,
        details: &super::moderation::ModerationDetails,
    ) -> ModuleResult<String> {
        let (helix, broadcaster, user) = self.operation_client(client_id, channel).await?;
        let target = if matches!(details.kind, "TIMEOUT" | "BAN" | "AUFHEBEN") {
            if details.by_id {
                details.user.clone()
            } else {
                helix
                    .get_user_by_login(&details.user)
                    .await?
                    .ok_or_else(|| invalid("Der Twitch-Benutzer wurde nicht gefunden."))?
                    .id
            }
        } else {
            String::new()
        };
        if matches!(details.kind, "TIMEOUT" | "BAN") && target == broadcaster {
            return Err(invalid("Der eigene Kanal kann nicht moderiert werden."));
        }
        let action = match details.kind {
            "TIMEOUT" | "BAN" => TwitchAction::Ban {
                id: target.clone(),
                duration: details.duration,
                reason: details.reason.clone(),
            },
            "AUFHEBEN" => TwitchAction::Unban { id: target.clone() },
            _ => TwitchAction::DeleteChat {
                message_id: details.message_id.clone(),
            },
        };
        helix
            .perform(action.request(&broadcaster, &user)?, None)
            .await?;
        Ok(target)
    }
    pub(super) async fn operation_client(
        &self,
        client_id: &str,
        channel: &str,
    ) -> ModuleResult<(TwitchHelixClient, String, String)> {
        let token = self.get_valid_token(client_id).await?;
        let helix =
            TwitchHelixClient::with_base_url(&self.helix_base, client_id, &token.access_token);
        let user = self
            .current_user()
            .await
            .ok_or_else(|| ModuleError::Message("Twitch nicht verbunden".into()))?;
        let broadcaster = if channel.is_empty() {
            user.id.clone()
        } else {
            helix
                .get_user_by_login(channel)
                .await?
                .ok_or_else(|| ModuleError::Message("Twitch-Kanal nicht gefunden".into()))?
                .id
        };
        Ok((helix, broadcaster, user.id))
    }
    pub async fn action(
        &self,
        client_id: &str,
        channel: &str,
        action: TwitchAction,
    ) -> ModuleResult<Value> {
        // Reject invalid drafts before token refresh or channel lookup.
        action.request("", "")?;
        let (helix, broadcaster, user) = self.operation_client(client_id, channel).await?;
        helix
            .perform(action.request(&broadcaster, &user)?, None)
            .await
    }
    pub async fn query(
        &self,
        client_id: &str,
        channel: &str,
        query: TwitchQuery,
        after: Option<String>,
    ) -> ModuleResult<Value> {
        if let TwitchQuery::Redemptions { reward_id, status } = &query {
            checked_text(reward_id, 200, "Reward-ID")?;
            checked_status(
                status.as_deref().unwrap_or("UNFULFILLED"),
                &["UNFULFILLED", "FULFILLED", "CANCELED"],
            )?;
        }
        let (helix, broadcaster, user) = self.operation_client(client_id, channel).await?;
        helix
            .perform(query.request(&broadcaster, &user), after)
            .await
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    use wiremock::{
        matchers::{body_json, header, method, path},
        Mock, MockServer, ResponseTemplate,
    };

    fn action(value: Value) -> TwitchAction {
        serde_json::from_value(value).unwrap()
    }

    #[test]
    fn voting_normalizes_csharp_fields_and_rejects_invalid_transitions() {
        let poll = action(json!({"action":"create_poll","title":" Question ","choices":[" A "," B "],"duration":1})).request("channel","user").unwrap();
        assert_eq!(
            poll.body.unwrap(),
            json!({"broadcaster_id":"channel","title":"Question","choices":[{"title":"A"},{"title":"B"}],"duration":15})
        );
        let prediction = action(json!({"action":"create_prediction","title":" Question ","outcomes":[" A "," B "],"window":9000})).request("channel","user").unwrap();
        assert_eq!(prediction.body.unwrap()["prediction_window"], 1800);
        for value in [
            json!({"action":"create_poll","title":"Q","choices":["only"],"duration":60}),
            json!({"action":"create_prediction","title":"Q","outcomes":["A"," "],"window":60}),
            json!({"action":"end_poll","id":"p","status":"RESOLVED"}),
            json!({"action":"end_prediction","id":"p","status":"RESOLVED"}),
            json!({"action":"update_redemption","rewardId":"r","id":"x","status":"ACTIVE"}),
        ] {
            assert!(
                action(value.clone()).request("channel", "user").is_err(),
                "{value}"
            );
        }
        let lock = action(json!({"action":"end_prediction","id":"p","status":"LOCKED","winningOutcomeId":"stale"})).request("channel","user").unwrap();
        assert!(lock.body.unwrap().get("winning_outcome_id").is_none());
    }

    #[tokio::test]
    async fn reward_patch_delete_and_redemption_paging_use_real_http_contracts() {
        use wiremock::matchers::query_param;
        let server = MockServer::start().await;
        let helix =
            TwitchHelixClient::with_base_url(format!("{}/", server.uri()), "client", "token");
        Mock::given(method("PATCH"))
            .and(path("/channel_points/custom_rewards"))
            .and(query_param("broadcaster_id", "channel"))
            .and(query_param("id", "r"))
            .and(body_json(json!({"is_paused":true,"prompt":"Input"})))
            .respond_with(ResponseTemplate::new(200).set_body_json(json!({"data":[{"id":"r"}]})))
            .expect(1)
            .mount(&server)
            .await;
        helix
            .perform(
                action(
                    json!({"action":"update_reward","id":"r","isPaused":true,"prompt":" Input "}),
                )
                .request("channel", "user")
                .unwrap(),
                None,
            )
            .await
            .unwrap();
        Mock::given(method("DELETE"))
            .and(path("/channel_points/custom_rewards"))
            .and(query_param("id", "r"))
            .and(query_param("broadcaster_id", "channel"))
            .respond_with(ResponseTemplate::new(204))
            .expect(1)
            .mount(&server)
            .await;
        assert!(helix
            .perform(
                action(json!({"action":"delete_reward","id":"r"}))
                    .request("channel", "user")
                    .unwrap(),
                None
            )
            .await
            .unwrap()
            .is_null());
        Mock::given(method("GET"))
            .and(path("/channel_points/custom_rewards/redemptions"))
            .and(query_param("reward_id", "r"))
            .and(query_param("status", "CANCELED"))
            .and(query_param("sort", "OLDEST"))
            .and(query_param("after", "cursor"))
            .respond_with(
                ResponseTemplate::new(200).set_body_json(json!({"data":[],"pagination":{}})),
            )
            .expect(1)
            .mount(&server)
            .await;
        let query: TwitchQuery = serde_json::from_value(
            json!({"query":"redemptions","rewardId":"r","status":"CANCELED"}),
        )
        .unwrap();
        helix
            .perform(query.request("channel", "user"), Some("cursor".into()))
            .await
            .unwrap();
    }

    #[test]
    fn reward_updates_are_partial_and_validate_fields() {
        let create =
            action(json!({"action":"create_reward","title":" Hi ","cost":0,"prompt":" Input "}))
                .request("channel", "user")
                .unwrap();
        assert_eq!(
            create.body.unwrap(),
            json!({"title":"Hi","cost":1,"prompt":"Input","is_enabled":true})
        );
        for value in [
            json!({"action":"update_reward","id":"r"}),
            json!({"action":"update_reward","id":"r","title":" "}),
            json!({"action":"update_reward","id":"r","cost":0}),
            json!({"action":"delete_reward","id":" "}),
        ] {
            assert!(
                action(value.clone()).request("channel", "user").is_err(),
                "{value}"
            );
        }
    }

    #[tokio::test]
    async fn voting_http_bodies_and_reward_ownership_errors_are_preserved() {
        let server = MockServer::start().await;
        let helix =
            TwitchHelixClient::with_base_url(format!("{}/", server.uri()), "client", "token");
        for (method_name, endpoint, value, body) in [
            (
                "POST",
                "/polls",
                json!({"action":"create_poll","title":" Q ","choices":["A","B"],"duration":60}),
                json!({"broadcaster_id":"channel","title":"Q","choices":[{"title":"A"},{"title":"B"}],"duration":60}),
            ),
            (
                "POST",
                "/predictions",
                json!({"action":"create_prediction","title":"Q","outcomes":["A","B"],"window":30}),
                json!({"broadcaster_id":"channel","title":"Q","outcomes":[{"title":"A"},{"title":"B"}],"prediction_window":30}),
            ),
            (
                "PATCH",
                "/predictions",
                json!({"action":"end_prediction","id":"p","status":"RESOLVED","winningOutcomeId":"a"}),
                json!({"broadcaster_id":"channel","id":"p","status":"RESOLVED","winning_outcome_id":"a"}),
            ),
        ] {
            Mock::given(method(method_name))
                .and(path(endpoint))
                .and(body_json(body))
                .respond_with(
                    ResponseTemplate::new(200).set_body_json(json!({"data":[{"id":"p"}]})),
                )
                .expect(1)
                .mount(&server)
                .await;
            assert_eq!(
                helix
                    .perform(action(value).request("channel", "user").unwrap(), None)
                    .await
                    .unwrap()["data"][0]["id"],
                "p"
            );
        }
        Mock::given(method("DELETE"))
            .and(path("/channel_points/custom_rewards"))
            .respond_with(
                ResponseTemplate::new(403)
                    .set_body_json(json!({"message":"Reward belongs to another app"})),
            )
            .mount(&server)
            .await;
        let error = helix
            .perform(
                action(json!({"action":"delete_reward","id":"r"}))
                    .request("channel", "user")
                    .unwrap(),
                None,
            )
            .await
            .unwrap_err();
        assert!(error
            .to_string()
            .contains("403: Reward belongs to another app"));
    }
    #[tokio::test]
    async fn chat_writes_have_actor_fields_and_report_rejected_messages() {
        let server = MockServer::start().await;
        let helix =
            TwitchHelixClient::with_base_url(format!("{}/", server.uri()), "client", "token");
        Mock::given(method("POST"))
            .and(path("/chat/messages"))
            .and(header("Client-Id", "client"))
            .and(body_json(
                json!({"broadcaster_id":"channel","sender_id":"user","message":"Hello"}),
            ))
            .respond_with(ResponseTemplate::new(200).set_body_json(
                json!({"data":[{"is_sent":false,"drop_reason":{"message":"Banned"}}]}),
            ))
            .expect(1)
            .mount(&server)
            .await;
        let error = helix
            .perform(
                TwitchAction::SendChat {
                    message: "Hello".into(),
                }
                .request("channel", "user")
                .unwrap(),
                None,
            )
            .await
            .unwrap_err();
        assert!(error.to_string().contains("Banned"));
        assert!(TwitchAction::SendChat {
            message: " ".into()
        }
        .request("channel", "user")
        .is_err());
    }
}
