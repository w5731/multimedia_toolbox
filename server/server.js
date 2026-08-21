const express = require('express');
const path = require('path');
const config = require('./config');
require('./db'); // 初始化数据库

const app = express();
app.disable('x-powered-by');
app.use(express.json({ limit: '256kb' }));
app.use(require('./middleware/session'));

app.use('/api/auth', require('./routes/auth'));
app.use('/api/client', require('./routes/client'));
app.use('/api/admin', require('./routes/admin'));

app.get('/api/health', (req, res) => res.json({ ok: true, time: require('./util').nowStr() }));

app.use(express.static(path.join(__dirname, 'public')));
app.get('/', (req, res) => res.redirect(req.teacher ? '/app.html' : '/login.html'));

app.use((err, req, res, next) => {
  console.error('[error]', err);
  res.status(500).json({ ok: false, error: '服务器内部错误' });
});

app.listen(config.port, () => {
  console.log(`多媒体任务看板服务器已启动: http://0.0.0.0:${config.port}`);
  console.log(`教师端入口: http://localhost:${config.port}/login.html`);
});
