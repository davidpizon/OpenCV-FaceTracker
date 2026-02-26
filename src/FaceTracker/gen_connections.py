import urllib.request

req=urllib.request.Request(
    'https://raw.githubusercontent.com/google/mediapipe/master/mediapipe/python/solutions/face_mesh_connections.py',
    headers={'User-Agent':'Mozilla/5.0'})
content=urllib.request.urlopen(req).read().decode()
exec(content.replace('frozenset','set'))

def to_cs(name, s):
    pairs = sorted(s)
    lines = ['    public static readonly (int,int)[] %s = {' % name]
    row = []
    for a,b in pairs:
        row.append('(%d,%d)' % (a,b))
        if len(row) == 8:
            lines.append('        ' + ', '.join(row) + ',')
            row = []
    if row:
        lines.append('        ' + ', '.join(row))
    lines.append('    };')
    return '\n'.join(lines)

datasets = [
    ('Tesselation', FACEMESH_TESSELATION),
    ('FaceOval', FACEMESH_FACE_OVAL),
    ('Lips', FACEMESH_LIPS),
    ('LeftEye', FACEMESH_LEFT_EYE),
    ('RightEye', FACEMESH_RIGHT_EYE),
    ('LeftEyebrow', FACEMESH_LEFT_EYEBROW),
    ('RightEyebrow', FACEMESH_RIGHT_EYEBROW),
    ('LeftIris', FACEMESH_LEFT_IRIS),
    ('RightIris', FACEMESH_RIGHT_IRIS),
    ('Nose', FACEMESH_NOSE),
]

output = []
for name, s in datasets:
    output.append(to_cs(name, s))

with open('connections.txt', 'w') as f:
    f.write('\n'.join(output))

print('Done')
