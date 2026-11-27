import json
import os
import socket
import urllib.error
import urllib.parse
import urllib.request


def verify():
    endpoint = os.environ["GATEWAY_VERIFY_ENDPOINT"].rstrip("/")
    hostname = urllib.parse.urlsplit(endpoint).hostname
    print(f"VERIFY gateway address={socket.gethostbyname(hostname)}", flush=True)
    query = urllib.parse.urlencode({
        "api-version": "2019-08-01",
        "resource": "https://ai.azure.com",
        "client_id": os.environ["AZURE_CLIENT_ID"],
    })
    identity_request = urllib.request.Request(
        os.environ["IDENTITY_ENDPOINT"] + "?" + query,
        headers={"X-IDENTITY-HEADER": os.environ["IDENTITY_HEADER"]},
    )
    with urllib.request.urlopen(identity_request, timeout=30) as response:
        token = json.load(response)["access_token"]
    for streaming in (False, True):
        payload = {
            "agent": {
                "type": "agent_reference",
                "name": os.environ["AIAgentId"],
                "version": os.environ.get("AIAgentVersion", "1"),
            },
            "input": "Reply with the word ready.",
            "stream": streaming,
        }
        request = urllib.request.Request(
            endpoint + "/openai/responses?api-version=2025-11-15-preview",
            data=json.dumps(payload).encode(),
            headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"},
        )
        with urllib.request.urlopen(request, timeout=180) as response:
            completed = None
            if streaming:
                for raw_line in response:
                    if raw_line.startswith(b"data: ") and raw_line.strip() != b"data: [DONE]":
                        event = json.loads(raw_line[6:])
                        if event.get("type") == "response.completed":
                            completed = event["response"]
            else:
                completed = json.load(response)
            if not completed or completed.get("status") != "completed":
                raise RuntimeError("Agent response did not complete")
            print(json.dumps({"stream": streaming, "status": response.status,
                              "response_id": completed["id"], "usage": completed.get("usage")}), flush=True)
    print("VERIFY gateway passed", flush=True)


if __name__ == "__main__":
    try:
        verify()
    except urllib.error.HTTPError as error:
        print(f"VERIFY gateway failed HTTP {error.code}", flush=True)
        raise SystemExit(1)
    except Exception as error:
        print(f"VERIFY gateway failed {type(error).__name__}", flush=True)
        raise SystemExit(1)