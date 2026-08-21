// 全局配置:可用环境变量覆盖
module.exports = {
  port: parseInt(process.env.PORT || '19283', 10),
  // 会话有效期(小时)
  sessionTtlHours: parseInt(process.env.SESSION_TTL_HOURS || '168', 10), // 默认 7 天
  // 心跳判定在线的秒数
  onlineSeconds: parseInt(process.env.ONLINE_SECONDS || '15', 10),
  // 叫号超过该分钟数未被客户端接收则标记为过期
  callExpireMinutes: parseInt(process.env.CALL_EXPIRE_MINUTES || '10', 10),
  // 配对码有效期(分钟)
  pairingCodeMinutes: parseInt(process.env.PAIRING_CODE_MINUTES || '30', 10),
  // 登录失败锁定:连续失败次数 / 锁定时长(分钟)
  loginMaxFails: parseInt(process.env.LOGIN_MAX_FAILS || '5', 10),
  loginLockMinutes: parseInt(process.env.LOGIN_LOCK_MINUTES || '5', 10),
  // 首次启动时创建的默认管理员(登录后请立即修改密码)
  defaultAdminUser: process.env.DEFAULT_ADMIN_USER || 'admin',
  defaultAdminPass: process.env.DEFAULT_ADMIN_PASS || 'admin123',
};
