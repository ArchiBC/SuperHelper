from pathlib import Path
from datetime import datetime, timezone
import json
import ghhops_server as hs

log = Path(__file__).resolve().parents[2] / 'artifacts' / 'hops-server-calls.jsonl'
hops = hs.Hops()


@hops.component(
    '/superhelper-test', name='SuperHelper compatibility test',
    description='Local numeric and tree transport test',
    inputs=[hs.HopsNumber('A', 'A', 'First number', default=6.0),
            hs.HopsNumber('B', 'B', 'Second number', default=7.0)],
    outputs=[hs.HopsNumber('Result', 'R', 'A times B plus 1')])
def solve(a, b):
    result = a * b + 1
    log.parent.mkdir(parents=True, exist_ok=True)
    with log.open('a') as stream:
        stream.write(json.dumps({'at': datetime.now(timezone.utc).isoformat(),
                                 'a': a, 'b': b, 'result': result}) + '\n')
    return result


# Hops 9 also requires the explicit names in its IO schema.
component = hops._components['/superhelper-test']
base_encode = component.encode


def encode_for_hops9():
    metadata = base_encode()
    metadata['InputNames'] = [p.name for p in component.inputs]
    metadata['OutputNames'] = [p.name for p in component.outputs]
    return metadata


component.encode = encode_for_hops9
if __name__ == '__main__':
    hops.start(address='127.0.0.1', port=18731, debug=False)
