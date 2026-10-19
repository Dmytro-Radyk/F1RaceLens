"""Read-only localhost viewer of actual saved models. No training endpoints."""

import json
import hashlib
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit

from .models import LightModel, artifact_hashes



def build_page(root: Path) -> str:
    data = {"generated": datetime.now(timezone.utc).isoformat(), "models": {}}
    for version in ("lightgbm-v1", "lightgbm-v2", "lightgbm-v3", "lightgbm-v4"):
        if not (root / f"reports/season-forecast-{version}.json").exists():
            continue
        directory = root / f"models/{version}"
        metadata = json.loads((directory / "metadata.json").read_text(encoding="utf-8"))
        if metadata.get('engine') != 'lightgbm':
            raise ValueError('Only LightGBM artifacts can be served')
        pace = LightModel.load(directory / 'pace.txt')
        risk = LightModel.load(directory / 'nonclassification.txt')
        forecast_path = root / f"reports/season-forecast-{version}.json"
        future = json.loads(forecast_path.read_text(encoding="utf-8"))
        if future["model"] != metadata["version"]:
            raise ValueError("Forecast model version differs from selected artifact")
        if future.get("artifact_sha256") != artifact_hashes(directory):
            raise ValueError("Model weights changed: regenerate season forecasts before viewing")
        model = {"version": metadata["version"], "pace_kind": metadata.get("pace_kind", "ranker"),
                 "pace_baseline_weight": pace.baseline_weight,
                 "risk_baseline_weight": metadata["calibration"].get("baseline_weight",0),
                 "pace_objective": pace.booster.params.get("objective", "regression"),
                 "pace_trees": pace.tree_count_, "risk_trees": risk.tree_count_,
                 "pace_depth": pace.get_all_params()["depth"], "risk_depth": risk.get_all_params()["depth"],
                 "learning_rate": round(pace.get_all_params()["learning_rate"], 3),
                 "train_through": metadata.get("final_train_through", 2022),
                 "features": len(metadata["features"]), "forecast": future, "races": {}}
        data["models"][version] = model
    if not data['models']:
        raise ValueError('Train and forecast LightGBM before starting the viewer')
    for key,path in [('post_qualifying','reports/post-qualifying.json'),('evaluation','reports/prospective-evaluation.json')]:
        if (root/path).exists():
            data[key] = json.loads((root/path).read_text(encoding='utf-8'))
    # Escape script terminators from any source-supplied name; DOM inserts text only.
    payload = json.dumps(data, ensure_ascii=False).replace("<", "\\u003c").replace("\u2028", "\\u2028").replace("\u2029", "\\u2029")
    return (root / "viewer/template.html").read_text(encoding="utf-8").replace("__MODEL_DATA__", payload)


def serve(root: Path, port: int = 4300) -> None:
    page = build_page(root).encode("utf-8")

    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            if urlsplit(self.path).path not in ("/", "/index.html"):
                self.send_error(404)
                return
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(page)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("Content-Security-Policy", "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'")
            self.end_headers()
            self.wfile.write(page)

    with ThreadingHTTPServer(("127.0.0.1", port), Handler) as server:
        print(f"Model viewer: http://127.0.0.1:{port} (read-only; Ctrl+C to stop)", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass
