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
    SearchCategories { text: String },
    SearchChannels { text: String },
    Redemptions { reward_id: String },
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
            Self::CreateReward{title,cost,prompt}=>Request::new(Method::POST,"channel_points/custom_rewards").param("broadcaster_id",broadcaster).body(json!({"title":title,"cost":cost,"prompt":prompt})),
            Self::UpdateRedemption{reward_id,id,status}=>Request::new(Method::PATCH,"channel_points/custom_rewards/redemptions").param("broadcaster_id",broadcaster).param("reward_id",reward_id).param("id",id).body(json!({"status":status})),
            Self::CreatePoll{title,choices,duration}=>Request::new(Method::POST,"polls").body(json!({"broadcaster_id":broadcaster,"title":title,"choices":choices.iter().map(|t|json!({"title":t})).collect::<Vec<_>>(),"duration":duration})),
            Self::EndPoll{id,status}=>Request::new(Method::PATCH,"polls").body(json!({"broadcaster_id":broadcaster,"id":id,"status":status})),
            Self::CreatePrediction{title,outcomes,window}=>Request::new(Method::POST,"predictions").body(json!({"broadcaster_id":broadcaster,"title":title,"outcomes":outcomes.iter().map(|t|json!({"title":t})).collect::<Vec<_>>(),"prediction_window":window})),
            Self::EndPrediction{id,status,winning_outcome_id}=>{let mut data=json!({"broadcaster_id":broadcaster,"id":id,"status":status});if let Some(id)=winning_outcome_id{data["winning_outcome_id"]=json!(id);}Request::new(Method::PATCH,"predictions").body(data)},
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
            Self::Redemptions { reward_id } => {
                Request::new(Method::GET, "channel_points/custom_rewards/redemptions")
                    .param("broadcaster_id", broadcaster)
                    .param("reward_id", reward_id)
                    .param("status", "UNFULFILLED")
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
    async fn operation_client(
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
