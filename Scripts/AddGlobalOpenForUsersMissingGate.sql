-- لمرة واحدة على قاعدة إنتاج: إضافة صلاحية Global.Open كصلاحية إضافية
-- لكل مستخدم نشط لا يملكها بعد (لا عبر UserExtraPermissions ولا عبر RolePermissions للأدوار المرتبطة به).
-- بدون Global.Open يفشل التحقق من Dashboard.Sales و Home.Index ومعظم شاشات العرض (انظر GlobalPermissionGates).
-- نفّذ على النسخة الاحتياطية أو بعد اختبار على نسخة تجريبية.

DECLARE @OpenId INT = (SELECT TOP 1 PermissionId FROM Permissions WHERE Code = N'Global.Open' AND IsActive = 1);
IF @OpenId IS NULL
BEGIN
    RAISERROR(N'لا يوجد سجل صلاحية Global.Open في Permissions — شغّل مزامنة/بذور الصلاحيات أولاً.', 16, 1);
    RETURN;
END;

INSERT INTO UserExtraPermissions (UserId, PermissionId, CreatedAt)
SELECT u.UserId, @OpenId, SYSUTCDATETIME()
FROM Users u
WHERE u.IsActive = 1
  AND NOT EXISTS (
      SELECT 1 FROM UserExtraPermissions x WHERE x.UserId = u.UserId AND x.PermissionId = @OpenId
  )
  AND NOT EXISTS (
      SELECT 1
      FROM UserRoles ur
      INNER JOIN RolePermissions rp ON rp.RoleId = ur.RoleId AND rp.PermissionId = @OpenId AND rp.IsAllowed = 1
      WHERE ur.UserId = u.UserId
  );
