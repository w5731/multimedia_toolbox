// 客户端安装包发布管理:teacher 上传 exe + 版本号,客户端轮询发现新版本后下载更新
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const DIR = path.join(__dirname, 'updates');
const EXE_PATH = path.join(DIR, 'MultimediaClient.exe');
const META_PATH = path.join(DIR, 'release.json');

// 以文件修改时间为缓存键:exe 被替换时才重新计算 sha256,避免每次心跳都哈希
let cache = { key: '', info: null };

function currentRelease() {
  try {
    const stExe = fs.statSync(EXE_PATH);
    let meta = {};
    let metaMtime = 0;
    try {
      meta = JSON.parse(fs.readFileSync(META_PATH, 'utf8'));
      metaMtime = fs.statSync(META_PATH).mtimeMs;
    } catch (e) { /* 无元数据视为未发布 */ }
    const key = stExe.mtimeMs + ':' + metaMtime + ':' + stExe.size;
    if (cache.key === key) return cache.info;
    const version = String(meta.version || '').trim();
    const info = version
      ? {
          version,
          size: stExe.size,
          sha256: crypto.createHash('sha256').update(fs.readFileSync(EXE_PATH)).digest('hex'),
          uploaded_by: String(meta.uploaded_by || ''),
          uploaded_at: String(meta.uploaded_at || ''),
        }
      : null;
    cache = { key, info };
    return info;
  } catch (e) {
    cache = { key: '', info: null };
    return null;
  }
}

// 原子写入:先写临时文件再改名,避免客户端读到写了一半的 exe
function saveRelease(version, buffer, uploadedBy, uploadedAt) {
  if (!fs.existsSync(DIR)) fs.mkdirSync(DIR, { recursive: true });
  const tmpExe = EXE_PATH + '.tmp';
  fs.writeFileSync(tmpExe, buffer);
  fs.renameSync(tmpExe, EXE_PATH);
  const meta = { version, uploaded_by: uploadedBy || '', uploaded_at: uploadedAt || '' };
  const tmpMeta = META_PATH + '.tmp';
  fs.writeFileSync(tmpMeta, JSON.stringify(meta, null, 2));
  fs.renameSync(tmpMeta, META_PATH);
  cache = { key: '', info: null }; // 立即失效,下一次读取重新计算
}

module.exports = { currentRelease, saveRelease, EXE_PATH };
