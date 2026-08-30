use crate::ModuleError;
use base64::{engine::general_purpose::STANDARD as BASE64, Engine};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use sha2::{Digest, Sha256};

pub const HELLO_OP: i32 = 0;
pub const IDENTIFY_OP: i32 = 1;
pub const IDENTIFIED_OP: i32 = 2;
pub const EVENT_OP: i32 = 5;
pub const REQUEST_OP: i32 = 6;
pub const REQUEST_RESPONSE_OP: i32 = 7;

pub const SUPPORTED_RPC_VERSION: u32 = 1;
pub const DEFAULT_EVENT_SUBSCRIPTIONS: i32 = 66031;
pub const MAX_PAYLOAD_BYTES: usize = 4 * 1024 * 1024;

#[derive(Debug, Clone)]
pub struct ObsReceivedEnvelope {
    pub op: i32,
    pub data: Value,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ObsHello {
    #[allow(dead_code)]
    pub obs_web_socket_version: String,
    pub rpc_version: u32,
    #[serde(default)]
    pub authentication: Option<ObsAuthenticationChallenge>,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ObsAuthenticationChallenge {
    pub challenge: String,
    pub salt: String,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ObsIdentified {
    #[allow(dead_code)]
    pub negotiated_rpc_version: u32,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ObsRequestResponse {
    #[allow(dead_code)]
    pub request_type: String,
    pub request_id: String,
    pub request_status: ObsRequestStatus,
    #[serde(default)]
    pub response_data: Value,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ObsRequestStatus {
    pub result: bool,
    pub code: i32,
    #[serde(default)]
    pub comment: Option<String>,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ObsSceneInfo {
    pub name: String,
    pub index: i32,
}

pub fn create_authentication_response(password: &str, salt: &str, challenge: &str) -> String {
    let secret_hash = Sha256::digest(format!("{password}{salt}").as_bytes());
    let secret = BASE64.encode(secret_hash);
    let auth_hash = Sha256::digest(format!("{secret}{challenge}").as_bytes());
    BASE64.encode(auth_hash)
}

pub fn decode_envelope(payload: &str) -> Result<ObsReceivedEnvelope, ModuleError> {
    if payload.len() > MAX_PAYLOAD_BYTES {
        return Err(ModuleError::Message(
            "OBS-Nachricht überschreitet das Größenlimit.".into(),
        ));
    }
    let root: Value = serde_json::from_str(payload).map_err(|e| {
        ModuleError::Message(format!("Ungültige OBS-WebSocket-Nachricht: {e}"))
    })?;
    decode_envelope_value(&root)
}

pub fn decode_envelope_value(root: &Value) -> Result<ObsReceivedEnvelope, ModuleError> {
    let op = root
        .get("op")
        .and_then(|v| v.as_i64())
        .ok_or_else(|| ModuleError::Message("Ungültige OBS-WebSocket-Nachricht.".into()))?
        as i32;
    let data = root
        .get("d")
        .cloned()
        .filter(|d| d.is_object())
        .ok_or_else(|| ModuleError::Message("Ungültige OBS-WebSocket-Nachricht.".into()))?;
    Ok(ObsReceivedEnvelope { op, data })
}

pub fn create_identify(hello: &ObsHello, password: Option<&str>) -> Result<Value, ModuleError> {
    if hello.rpc_version < SUPPORTED_RPC_VERSION {
        return Err(ModuleError::Message(format!(
            "OBS RPC-Version {} wird nicht unterstützt.",
            hello.rpc_version
        )));
    }

    let authentication = if let Some(auth) = &hello.authentication {
        let password = password
            .filter(|p| !p.trim().is_empty())
            .ok_or_else(|| ModuleError::Message("OBS verlangt ein WebSocket-Passwort.".into()))?;
        if auth.salt.trim().is_empty() || auth.challenge.trim().is_empty() {
            return Err(ModuleError::Message(
                "OBS sendete eine ungültige Authentifizierungsanforderung.".into(),
            ));
        }
        Some(create_authentication_response(
            password,
            &auth.salt,
            &auth.challenge,
        ))
    } else {
        None
    };

    let mut d = json!({
        "rpcVersion": SUPPORTED_RPC_VERSION,
        "eventSubscriptions": DEFAULT_EVENT_SUBSCRIPTIONS,
    });
    if let Some(authentication) = authentication {
        d["authentication"] = Value::String(authentication);
    }

    Ok(json!({
        "op": IDENTIFY_OP,
        "d": d
    }))
}

pub fn build_request(request_type: &str, request_id: &str, request_data: Option<Value>) -> Value {
    let mut d = json!({
        "requestType": request_type,
        "requestId": request_id,
    });
    if let Some(data) = request_data {
        d["requestData"] = data;
    }
    json!({
        "op": REQUEST_OP,
        "d": d
    })
}

pub fn parse_scene_list(response_data: &Value) -> Vec<ObsSceneInfo> {
    let mut scenes: Vec<ObsSceneInfo> = response_data
        .get("scenes")
        .and_then(|s| s.as_array())
        .map(|arr| {
            arr.iter()
                .map(|scene| ObsSceneInfo {
                    name: scene
                        .get("sceneName")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string(),
                    index: scene
                        .get("sceneIndex")
                        .and_then(|v| v.as_i64())
                        .unwrap_or(0) as i32,
                })
                .collect()
        })
        .unwrap_or_default();
    scenes.sort_by_key(|s| s.index);
    scenes
}

pub fn parse_current_program_scene(data: &Value) -> Option<String> {
    let event_type = data.get("eventType")?.as_str()?;
    if event_type != "CurrentProgramSceneChanged" {
        return None;
    }
    let name = data
        .get("eventData")?
        .get("sceneName")?
        .as_str()?
        .trim();
    if name.is_empty() {
        None
    } else {
        Some(name.to_string())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn fixture(name: &str) -> String {
        let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("src/obs/fixtures")
            .join(name);
        std::fs::read_to_string(path).expect("fixture")
    }

    #[test]
    fn authentication_response_is_deterministic() {
        let first = create_authentication_response("password", "salt", "challenge");
        let second = create_authentication_response("password", "salt", "challenge");
        assert_eq!(first, second);
        assert!(!first.is_empty());
    }

    #[test]
    fn different_passwords_produce_different_responses() {
        let first = create_authentication_response("password-a", "salt", "challenge");
        let second = create_authentication_response("password-b", "salt", "challenge");
        assert_ne!(first, second);
    }

    #[test]
    fn hello_creates_authenticated_identify_for_rpc_version_one() {
        let envelope = decode_envelope(&fixture("hello-auth.json")).unwrap();
        let hello: ObsHello = serde_json::from_value(envelope.data.clone()).unwrap();
        let identify = create_identify(&hello, Some("contract-password")).unwrap();

        assert_eq!(envelope.op, HELLO_OP);
        assert_eq!(hello.obs_web_socket_version, "5.6.0");
        assert_eq!(identify["op"], IDENTIFY_OP);
        assert_eq!(identify["d"]["rpcVersion"], SUPPORTED_RPC_VERSION);
        assert_eq!(
            identify["d"]["authentication"],
            "hBX7/Dl9VT/Ag1a8AGOXSUYIpRRQmqUj/UwWwgabh/k="
        );
        assert_eq!(
            identify["d"]["eventSubscriptions"],
            DEFAULT_EVENT_SUBSCRIPTIONS
        );
    }

    #[test]
    fn hello_without_auth_omits_authentication_field() {
        let envelope = decode_envelope(&fixture("hello-no-auth.json")).unwrap();
        let hello: ObsHello = serde_json::from_value(envelope.data.clone()).unwrap();
        let identify = create_identify(&hello, None).unwrap();
        assert_eq!(identify["op"], IDENTIFY_OP);
        assert!(identify["d"].get("authentication").is_none());
        assert_eq!(
            identify["d"]["eventSubscriptions"],
            DEFAULT_EVENT_SUBSCRIPTIONS
        );
    }

    #[test]
    fn identified_maps_negotiated_rpc_version() {
        let envelope = decode_envelope(&fixture("identified.json")).unwrap();
        let identified: ObsIdentified = serde_json::from_value(envelope.data).unwrap();
        assert_eq!(envelope.op, IDENTIFIED_OP);
        assert_eq!(identified.negotiated_rpc_version, 1);
    }

    #[test]
    fn request_response_success_maps_status() {
        let envelope = decode_envelope(&fixture("request-response-success.json")).unwrap();
        let response: ObsRequestResponse = serde_json::from_value(envelope.data).unwrap();
        assert_eq!(envelope.op, REQUEST_RESPONSE_OP);
        assert!(response.request_id.starts_with("request-"));
        assert!(response.request_status.result);
        assert_eq!(response.request_status.code, 100);
        assert!(response.request_status.comment.is_none());
        assert!(response.response_data.is_object());
    }

    #[test]
    fn request_response_failure_maps_status() {
        let envelope = decode_envelope(&fixture("request-response-failure.json")).unwrap();
        let response: ObsRequestResponse = serde_json::from_value(envelope.data).unwrap();
        assert_eq!(envelope.op, REQUEST_RESPONSE_OP);
        assert!(!response.request_status.result);
        assert_eq!(response.request_status.code, 600);
        assert_eq!(
            response.request_status.comment.as_deref(),
            Some("No source was found by the name of `Missing`.")
        );
    }

    #[test]
    fn get_scene_list_fixture_parses_scenes() {
        let envelope = decode_envelope(&fixture("get-scene-list.json")).unwrap();
        let response: ObsRequestResponse = serde_json::from_value(envelope.data).unwrap();
        let scenes = parse_scene_list(&response.response_data);
        assert_eq!(scenes.len(), 3);
        assert_eq!(scenes[0].name, "Start");
        assert_eq!(scenes[1].name, "Live");
        assert_eq!(scenes[2].index, 2);
    }

    #[test]
    fn current_program_scene_changed_extracts_scene_name() {
        let envelope = decode_envelope(&fixture("current-program-scene-changed.json")).unwrap();
        assert_eq!(envelope.op, EVENT_OP);
        let scene = parse_current_program_scene(&envelope.data).unwrap();
        assert_eq!(scene, "Game");
    }

    #[test]
    fn handshake_rejects_unsupported_rpc_version() {
        let hello = ObsHello {
            obs_web_socket_version: "5.0.0".into(),
            rpc_version: 0,
            authentication: None,
        };
        let err = create_identify(&hello, None).unwrap_err();
        assert!(err.to_string().contains("RPC"));
    }

    #[test]
    fn invalid_envelope_is_rejected() {
        for payload in [
            "{}",
            "{\"op\":5}",
            "{\"op\":\"5\",\"d\":{}}",
            "{\"op\":5,\"d\":[]}",
            "not-json",
        ] {
            assert!(decode_envelope(payload).is_err(), "payload={payload}");
        }
    }

    #[test]
    fn oversized_envelope_is_rejected() {
        let payload = format!(
            "{{\"op\":5,\"d\":{{\"value\":\"{}\"}}}}",
            "x".repeat(MAX_PAYLOAD_BYTES)
        );
        assert!(decode_envelope(&payload).is_err());
    }

    #[test]
    fn build_set_scene_request_shape() {
        let msg = build_request(
            "SetCurrentProgramScene",
            "req-1",
            Some(json!({ "sceneName": "Live" })),
        );
        assert_eq!(msg["op"], REQUEST_OP);
        assert_eq!(msg["d"]["requestType"], "SetCurrentProgramScene");
        assert_eq!(msg["d"]["requestData"]["sceneName"], "Live");
        assert_eq!(msg["d"]["requestId"], "req-1");
    }
}
