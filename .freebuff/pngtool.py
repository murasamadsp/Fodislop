#!/usr/bin/env python3
"""Decode a PNG (all filter types) into RGB pixels and analyze/render it."""
import sys, zlib, struct

def decode_png(path):
    data = open(path, 'rb').read()
    assert data[:8] == b'\x89PNG\r\n\x1a\n', 'not a PNG'
    pos = 8
    idat = b''
    w = h = bitdepth = coltype = None
    while pos < len(data):
        length, ctype = struct.unpack('>I4s', data[pos:pos+8])
        chunk = data[pos+8:pos+8+length]
        if ctype == b'IHDR':
            w, h, bitdepth, coltype = struct.unpack('>IIBB', chunk[:10])
        elif ctype == b'IDAT':
            idat += chunk
        pos += 12 + length
    if coltype == 2:      # RGB
        ch = 3
    elif coltype == 6:    # RGBA
        ch = 4
    elif coltype == 0:    # gray
        ch = 1
    else:
        raise ValueError(f'unsupported color type {coltype}')
    raw = zlib.decompress(idat)
    stride = w * ch
    out = bytearray(h * stride)
    prev = bytearray(stride)
    p = 0
    for y in range(h):
        f = raw[p]; p += 1
        line = bytearray(raw[p:p+stride]); p += stride
        if f == 1:  # Sub
            for i in range(ch, stride):
                line[i] = (line[i] + line[i-ch]) & 0xFF
        elif f == 2:  # Up
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif f == 3:  # Average
            for i in range(stride):
                a = line[i-ch] if i >= ch else 0
                b = prev[i]
                line[i] = (line[i] + ((a + b) >> 1)) & 0xFF
        elif f == 4:  # Paeth
            for i in range(stride):
                a = line[i-ch] if i >= ch else 0
                b = prev[i]
                c = prev[i-ch] if i >= ch else 0
                pa, pb, pc = abs(b-c), abs(a-c), abs(a+b-2*c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xFF
        out[y*stride:(y+1)*stride] = line
        prev = line
    return w, h, ch, bytes(out)

def make_html(path, out_html):
    w, h, ch, px = decode_png(path)
    # downscale to max 960 wide for embedding
    sw = max(1, w // 960 + (1 if w % 960 else 0))
    sh = max(1, h // 540 + (1 if h % 540 else 0))
    step = max(sw, sh)
    tw, th = w // step, h // step
    import base64, io
    # build PNG via raw zlib (filters=0 rows)
    stride = tw * 3
    raw = b''.join(b'\x00' + bytes(row) for row in gen_rows(tw, th, w, h, ch, px, step))
    def chunk(t, d):
        return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', tw, th, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw, 6)) + chunk(b'IEND', b'')
    b64 = base64.b64encode(png).decode()
    with open(out_html, 'w') as f:
        f.write(f'<html><body style="margin:0;background:#111"><img src="data:image/png;base64,{b64}" style="width:100%"></body></html>')
    print(f'wrote {out_html} ({w}x{h} -> {tw}x{th})')

def gen_rows(tw, th, w, h, ch, px, step):
    for ty in range(th):
        row = bytearray()
        for tx in range(tw):
            # average block
            r = g = b = n = 0
            for yy in range(ty*step, min((ty+1)*step, h)):
                for xx in range(tx*step, min((tx+1)*step, w)):
                    i = (yy * w + xx) * ch
                    r += px[i]; g += px[i+1]; b += px[i+2]; n += 1
            row += bytes((r//n, g//n, b//n))
        yield bytes(row)

def histogram(path, top=12):
    w, h, ch, px = decode_png(path)
    from collections import Counter
    c = Counter()
    for i in range(0, len(px), ch):
        c[(px[i] >> 4, px[i+1] >> 4, px[i+2] >> 4)] += 1
    total = len(px) // ch
    print(f'{w}x{h} ch={ch} total={total}')
    for col, n in c.most_common(top):
        print(f'  #{col[0]:02x}{col[1]:02x}{col[2]:02x}  {100.0*n/total:5.2f}%')

if __name__ == '__main__':
    cmd = sys.argv[1]
    if cmd == 'html':
        make_html(sys.argv[2], sys.argv[3])
    elif cmd == 'hist':
        histogram(sys.argv[2])
