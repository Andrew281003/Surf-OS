"""Offline seed-manifest benchmark. No network, Firestore, or Drive access."""
import argparse
import concurrent.futures
import hashlib
import json
import os
import pathlib
import statistics
import time
import tracemalloc

ROOT = pathlib.Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "surfcloud-seed" / "packages.json"


def percentile(values, fraction):
    ordered = sorted(values)
    return ordered[max(0, min(len(ordered) - 1, int((len(ordered) - 1) * fraction)))]


def operation(kind, payload):
    start = time.perf_counter_ns()
    if kind == "repository_listing":
        result = len(json.loads(payload)["packages"])
    elif kind == "package_search":
        result = sum("surf" in p["name"].lower() for p in json.loads(payload)["packages"])
    elif kind == "package_integrity":
        result = len(hashlib.sha256(payload).digest())
    else:
        raise ValueError(kind)
    return (time.perf_counter_ns() - start) / 1e6, result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--iterations", type=int, default=1000)
    parser.add_argument("--clients", type=int, nargs="+", default=[1, 10, 50, 100])
    args = parser.parse_args()
    if args.iterations < 1 or any(n < 1 for n in args.clients):
        parser.error("iterations and clients must be positive")
    payload = MANIFEST.read_bytes()
    print(json.dumps({"runtime": "Python", "seed_bytes": len(payload), "pid": os.getpid(), "operations": "local surrogate only"}))
    for kind in ("repository_listing", "package_search", "package_integrity"):
        for clients in args.clients:
            tracemalloc.start()
            cpu_start = time.process_time()
            wall_start = time.perf_counter()
            errors = 0
            latencies = []
            with concurrent.futures.ThreadPoolExecutor(max_workers=clients) as pool:
                futures = [pool.submit(operation, kind, payload) for _ in range(args.iterations)]
                for future in concurrent.futures.as_completed(futures):
                    try:
                        latency, _ = future.result()
                        latencies.append(latency)
                    except Exception:
                        errors += 1
            wall = time.perf_counter() - wall_start
            cpu = time.process_time() - cpu_start
            _, peak = tracemalloc.get_traced_memory()
            tracemalloc.stop()
            print(json.dumps({"operation": kind, "clients": clients, "requests": args.iterations,
                "p50_ms": round(percentile(latencies, .50), 4) if latencies else None,
                "p95_ms": round(percentile(latencies, .95), 4) if latencies else None,
                "p99_ms": round(percentile(latencies, .99), 4) if latencies else None,
                "throughput_ops_s": round(len(latencies) / wall, 1), "error_rate": round(errors / args.iterations, 4),
                "cpu_seconds": round(cpu, 4), "peak_traced_bytes": peak,
                "input_bytes_processed": len(payload) * args.iterations, "network_bytes": 0, "database_ops": 0}))


if __name__ == "__main__":
    main()
