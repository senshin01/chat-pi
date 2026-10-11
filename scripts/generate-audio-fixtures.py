# Original synthesized test audio; requires Python 3 and FFmpeg 7.1.5.
from pathlib import Path
import math, wave, subprocess, struct, zlib, random
root=Path(__file__).resolve().parents[1] / 'tests' / 'Fixtures'
root.mkdir(parents=True, exist_ok=True)
def wav(name,rate,bits,channels):
    data=bytearray()
    for i in range(rate//2):
        for c in range(channels):
            if bits==24:
                v=round(math.sin(2*math.pi*(440+c*219)*i/rate)*2000000)+(i*37+c*11)%251
            elif bits==16:
                v=round(math.sin(2*math.pi*(440+c*219)*i/rate)*12000)
            else:
                v=128+round(math.sin(2*math.pi*440*i/rate)*70)
            data+=v.to_bytes(bits//8,'little',signed=bits!=8)
    with wave.open(str(root/name),'wb') as f:
        f.setnchannels(channels);f.setsampwidth(bits//8);f.setframerate(rate);f.writeframes(data)
wav('reference16.wav',44100,16,2)
wav('reference24.wav',96000,24,2)
wav('reference8.wav',48000,8,1)
def chunk(t,p):return struct.pack('>I',len(p))+t+p+struct.pack('>I',zlib.crc32(t+p)&0xffffffff)
(root/'cover.png').write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',16,16,8,2,0,0,0))+chunk(b'IDAT',zlib.compress((b'\0'+b'\x21\x6d\x48'*16)*16))+chunk(b'IEND',b''))
def encode(src,out,codec,extra=(),cover=False):
    args=['ffmpeg','-hide_banner','-loglevel','error','-y','-i',str(root/src)]
    if cover:args+=['-i',str(root/'cover.png'),'-map','0:a','-map','1:v','-c:v','copy','-disposition:v:0','attached_pic']
    args+=['-c:a',codec,*extra,'-metadata','title=Fixture Song','-metadata','artist=Fixture Artist','-metadata','album=Fixture Album',str(root/out)]
    subprocess.run(args,check=True)
for bits in [16,24]:
    encode(f'reference{bits}.wav',f'lossless{bits}.flac','flac',cover=bits==24)
    encode(f'reference{bits}.wav',f'alac{bits}.m4a','alac',cover=bits==24)
    encode(f'reference{bits}.wav',f'pcm{bits}.aiff',f'pcm_s{bits}be')
encode('reference8.wav','pcm8.aif','pcm_s8')
encode('reference16.wav','aac.m4a','aac',['-b:a','128k'])
encode('reference16.wav','aac.aac','aac',['-b:a','128k'])
encode('reference16.wav','vorbis.ogg','libvorbis',['-q:a','5'])
encode('reference8.wav','vorbis-mono.oga','libvorbis',['-q:a','5'])
encode('reference16.wav','wma.wma','wmav2',['-b:a','128k'])
rng = random.Random(12345)
with wave.open(str(root/'vorbis-seek-reference.wav'), 'wb') as f:
    f.setnchannels(1); f.setsampwidth(2); f.setframerate(44100)
    f.writeframes(b''.join(struct.pack('<h', rng.randrange(-10000, 10001)) for _ in range(44100 * 3)))
encode('vorbis-seek-reference.wav', 'vorbis-seek.ogg', 'libvorbis', ['-q:a', '5'])
(root/'vorbis-seek-reference.wav').unlink()
for p in root.iterdir():print(p.name,p.stat().st_size)
