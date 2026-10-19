import json
import math
from datetime import datetime, timezone
from pathlib import Path

from ..source import request_json


def parse_standings(page: dict, season: int, through: int, constructors: bool) -> list[dict]:
    data = page["MRData"]
    table = data["StandingsTable"]
    lists = table["StandingsLists"]
    if int(data["offset"]) != 0 or len(lists) != 1 or int(table["season"]) != season:
        raise ValueError("Invalid standings snapshot")
    snapshot = lists[0]
    if int(snapshot["season"]) != season or int(snapshot["round"]) != through:
        raise ValueError("Standings cutoff differs from known results")
    rows = snapshot["ConstructorStandings" if constructors else "DriverStandings"]
    if len(rows) != int(data["total"]) or not rows:
        raise ValueError("Incomplete standings pagination")
    result = []
    for row in rows:
        if not str(row["positionText"]).isdigit():
            raise ValueError("Excluded or ambiguous standings not supported")
        person = row["Constructor" if constructors else "Driver"]
        result.append({"id": person["constructorId" if constructors else "driverId"],
                       "name": person["name"] if constructors else person["givenName"] + " " + person["familyName"],
                       "points": float(row["points"]), "position": int(row["position"])})
    if len({r["id"] for r in result}) != len(result) or sorted(r["position"] for r in result) != list(range(1, len(result) + 1)):
        raise ValueError("Invalid standings participants")
    if any(not math.isfinite(r["points"]) for r in result):
        raise ValueError("Invalid standings points")
    return result


def fetch_input(root: Path, season: int, through: int, refresh: bool = False) -> dict:
    target = root / f"data/forecast-input-{season}-{through}.json"
    if target.exists() and not refresh:
        return json.loads(target.read_text(encoding="utf-8"))
    calendar_page = request_json(f"{season}.json?limit=100")
    data = calendar_page["MRData"]
    calendar = data["RaceTable"]["Races"]
    if int(data["offset"]) != 0 or len(calendar) != int(data["total"]) or int(data["RaceTable"]["season"]) != season:
        raise ValueError("Incomplete calendar")
    if any(int(r["season"]) != season for r in calendar) or sorted(int(r["round"]) for r in calendar) != list(range(1, len(calendar) + 1)):
        raise ValueError("Invalid calendar rounds")
    drivers_page = request_json(f"{season}/{through}/driverStandings.json?limit=100")
    teams_page = request_json(f"{season}/{through}/constructorStandings.json?limit=100")
    result = {"season": season, "through": through, "fetched_at": datetime.now(timezone.utc).isoformat(),
              "drivers": parse_standings(drivers_page, season, through, False),
              "teams": parse_standings(teams_page, season, through, True),
              "calendar": calendar, "source": "Jolpica; official standings pinned to observed round"}
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8")
    return result
