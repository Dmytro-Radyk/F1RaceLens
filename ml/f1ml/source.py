"""Rate-limited Jolpica downloads with validated pagination and local snapshots."""

import hashlib
import json
import time
import urllib.error
import urllib.request
from email.utils import parsedate_to_datetime
from datetime import datetime, timezone
from pathlib import Path

BASE_URL = "https://api.jolpi.ca/ergast/f1/"


def retry_delay(header, attempt, now=None):
    if header:
        try:
            return max(0, int(header))
        except ValueError:
            try:
                stamp = parsedate_to_datetime(header)
                if stamp.tzinfo:
                    return max(0, (stamp-(now or datetime.now(timezone.utc))).total_seconds())
            except (ValueError, TypeError, OverflowError):
                pass
    return min(60, 2 ** (attempt + 1))


def request_json(path: str) -> dict:
    for attempt in range(4):
        time.sleep(0.4)
        request = urllib.request.Request(BASE_URL + path, headers={"User-Agent": "F1RaceLens-ML/0.1"})
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            if error.code not in (429, 500, 502, 503, 504) or attempt == 3:
                raise
            delay = retry_delay(error.headers.get("Retry-After"), attempt)
            if delay > 120:
                raise  # Do not retry earlier than a long server cooldown.
            time.sleep(delay)
        except (TimeoutError, urllib.error.URLError):
            if attempt == 3:
                raise
            time.sleep(2 ** (attempt + 1))
    raise RuntimeError("Unreachable retry state")


def normalize(pages: list[dict], year: int) -> list[dict]:
    records = []
    expected_total = None
    seen = set()
    for page in pages:
        data = page["MRData"]
        total = int(data["total"])
        if not 0 <= total <= 2000 or int(data["offset"]) != len(records):
            raise ValueError("Invalid result pagination")
        if expected_total is not None and total != expected_total:
            raise ValueError("Result total changed during download")
        expected_total = total
        table = data["RaceTable"]
        if int(table["season"]) != year:
            raise ValueError("Unexpected source year")
        before = len(records)
        for race in table["Races"]:
            if int(race["season"]) != year or not 1 <= int(race["round"]) <= 40:
                raise ValueError("Invalid race metadata")
            for result in race["Results"]:
                driver = result["Driver"]["driverId"]
                key = (int(race["round"]), driver)
                if key in seen:
                    raise ValueError("Duplicate race entry")
                seen.add(key)
                position = int(result["position"])
                if not 1 <= position <= 40:
                    raise ValueError("Invalid classification position")
                records.append({
                    "season": year, "round": key[0], "date": race["date"],
                    "race_name": race["raceName"], "circuit": race["Circuit"]["circuitId"],
                    "driver": driver, "name": result["Driver"]["givenName"] + " " + result["Driver"]["familyName"],
                    "team": result["Constructor"]["constructorId"],
                    "position": position, "classified": str(result["positionText"]).isdigit(),
                    "points": float(result["points"]),
                })
        if len(records) == before and len(records) < total:
            raise ValueError("Empty intermediate page")
    if expected_total is None or len(records) != expected_total:
        raise ValueError("Incomplete season snapshot")
    groups = {}
    for row in records:
        groups.setdefault(row["round"], []).append(row)
    for group in groups.values():
        if len(group) < 10 or sorted(r["position"] for r in group) != list(range(1, len(group) + 1)):
            raise ValueError("Incomplete or ambiguous classification")
        if len({r["date"] for r in group}) != 1 or len({r["circuit"] for r in group}) != 1:
            raise ValueError("Inconsistent race metadata")
    if groups and sorted(groups) != list(range(1, max(groups) + 1)):
        raise ValueError("Missing race round")
    return records


def fetch_season(year: int, destination: Path, refresh: bool = False) -> list[dict]:
    destination.mkdir(parents=True, exist_ok=True)
    target = destination / f"results-{year}.json"
    if target.exists() and not refresh:
        snapshot = json.loads(target.read_text(encoding="utf-8"))
        return normalize(snapshot["pages"], year)
    pages = []
    offset = 0
    while True:
        page = request_json(f"{year}/results.json?limit=100&offset={offset}")
        data = page["MRData"]
        count = sum(len(r["Results"]) for r in data["RaceTable"]["Races"])
        if int(data["offset"]) != offset or (count == 0 and offset < int(data["total"])):
            raise ValueError("Invalid source page")
        pages.append(page)
        offset += count
        if offset >= int(data["total"]):
            break
    rows = normalize(pages, year)
    snapshot = {"source": BASE_URL, "fetched_at": datetime.now(timezone.utc).isoformat(), "pages": pages}
    temporary = target.with_suffix(".tmp")
    temporary.write_text(json.dumps(snapshot, ensure_ascii=False), encoding="utf-8")
    temporary.replace(target)
    print(f"Downloaded {year}: {len(rows)} entries / {len(set(r['round'] for r in rows))} races", flush=True)
    return rows


def snapshot_hash(directory: Path) -> str:
    digest = hashlib.sha256()
    for path in sorted(directory.glob("results-*.json")):
        digest.update(path.name.encode())
        digest.update(path.read_bytes())
    return digest.hexdigest()
