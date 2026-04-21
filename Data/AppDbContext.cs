
using ERP.Data.Seed;
using ERP.Models;
using ERP.Seed;
using Microsoft.AspNetCore.Http;   // للوصول لاسم المستخدم الحالي
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;                 // لاستعمال LINQ في TrackProductPriceChanges
using System.Reflection.Emit;


namespace ERP.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<ERP.Models.Governorate> Governorates { get; set; } = null!;
        public DbSet<ERP.Models.District> Districts { get; set; } = null!;
        public DbSet<ERP.Models.City> Cities { get; set; } = null!;
        public DbSet<ERP.Models.Area> Areas { get; set; } = null!;
        public DbSet<ERP.Models.Customer> Customers { get; set; } = null!;

        public DbSet<ERP.Models.Product> Products { get; set; } = null!;   // جدول الأصناف
        public DbSet<Branch> Branches { get; set; } = null!;
        public DbSet<ERP.Models.Warehouse> Warehouses { get; set; } = null!;
        public DbSet<Category> Categories { get; set; } = null!;
        public DbSet<ProductPriceHistory> ProductPriceHistories => Set<ProductPriceHistory>();
        public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();                 // جدول فواتير البيع (الهيدر)
        public DbSet<SalesInvoiceLine> SalesInvoiceLines => Set<SalesInvoiceLine>();
        public DbSet<StockLedger> StockLedger => Set<StockLedger>();
        public DbSet<StockFifoMap> StockFifoMap => Set<StockFifoMap>();
        public DbSet<Batch> Batches => Set<Batch>();
        public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();
        public DbSet<SalesReturnLine> SalesReturnLines => Set<SalesReturnLine>();
        public DbSet<PrintHeaderSetting> PrintHeaderSettings => Set<PrintHeaderSetting>();
        public DbSet<ERP.Models.DocumentSeries> DocumentSeries => Set<ERP.Models.DocumentSeries>();
        public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();
        public DbSet<PRLine> PRLines => Set<PRLine>();
        public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
        public DbSet<PILine> PILines => Set<PILine>();
        public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
        public DbSet<PurchaseReturnLine> PurchaseReturnLines => Set<PurchaseReturnLine>();
        public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
        public DbSet<SOLine> SOLines => Set<SOLine>();
        public DbSet<Account> Accounts { get; set; } = null!;
        public DbSet<LedgerEntry> LedgerEntries { get; set; } = default!;
        public DbSet<CashReceipt> CashReceipts { get; set; } = default!;
        public DbSet<CashPayment> CashPayments { get; set; } = default!;
        public DbSet<DebitNote> DebitNotes { get; set; } = default!;
        public DbSet<CreditNote> CreditNotes { get; set; } = default!;

        // ===== جداول المستخدمين والصلاحيات =====
        public DbSet<User> Users { get; set; } = null!;                        // جدول المستخدمين
        public DbSet<Role> Roles { get; set; } = null!;                        // جدول الأدوار
        public DbSet<Permission> Permissions { get; set; } = null!;           // جدول الصلاحيات
        public DbSet<UserRole> UserRoles { get; set; } = null!;               // ربط المستخدمين بالأدوار
        public DbSet<RolePermission> RolePermissions { get; set; } = null!;   // ربط الأدوار بالصلاحيات
        public DbSet<UserDeniedPermission> UserDeniedPermissions { get; set; } = null!; // استثناءات الصلاحيات للمستخدم
        public DbSet<UserExtraPermissions> UserExtraPermissions { get; set; } = null!;
        public DbSet<UserActivityLog> UserActivityLogs { get; set; } = null!; // سجل نشاط المستخدمين

        // ===== إخفاء الحسابات عن المستخدم =====
        public DbSet<UserAccountVisibilityOverride> UserAccountVisibilityOverrides { get; set; } = null!;
        /// <summary>الحسابات المسموح رؤيتها لكل دور (يُدمج مع إعداد المستخدم إن وُجد).</summary>
        public DbSet<RoleAccountVisibilityOverride> RoleAccountVisibilityOverrides { get; set; } = null!;

        // جداول السياسات
        public DbSet<Policy> Policies { get; set; } = null!;                    // جدول السياسات العامة
        public DbSet<WarehousePolicyRule> WarehousePolicyRules { get; set; } = null!; // قواعد السياسة لكل مخزن
        public DbSet<ProductGroup> ProductGroups { get; set; } = null!;         // مجموعات الأصناف
        public DbSet<ProductGroupPolicy> ProductGroupPolicies { get; set; } = null!; // سياسة كل مجموعة أصناف

        /// <summary>جدول الخصم اليدوي للبيع (تقرير أرصدة الأصناف + المبيعات).</summary>
        public DbSet<ProductDiscountOverride> ProductDiscountOverrides { get; set; } = null!;

        // ========================
        // جداول تسويات المخزون (الهيدر + السطور)
        // ========================

        // جدول هيدر التسوية: مستند جرد/تسوية للمخزون في مخزن معيّن
        public DbSet<StockAdjustment> StockAdjustments { get; set; }   // متغير: جدول هيدر التسويات

        // جدول سطور التسوية: كل سطر يمثل صنف (وباتش اختياري) قبل/بعد التسوية
        public DbSet<StockAdjustmentLine> StockAdjustmentLines { get; set; }   // متغير: جدول سطور التسويات

        // جدول التحويلات بين المخازن (الهيدر)
        public DbSet<StockTransfer> StockTransfers { get; set; } = null!;   // متغير: جدول رؤوس التحويلات المخزنية

        // جدول سطور التحويلات
        public DbSet<StockTransferLine> StockTransferLines { get; set; } = null!; // متغير: جدول سطور الأصناف لكل تحويل
        public DbSet<ProductBonusGroup> ProductBonusGroups { get; set; } = default!;
        public DbSet<ERP.Models.StockBatch> StockBatches { get; set; } = default!;

        // ===== خط السير =====
        public DbSet<ProductClassification> ProductClassifications { get; set; } = null!;
        public DbSet<ERP.Models.Route> Routes { get; set; } = null!;
        public DbSet<SalesInvoiceRoute> SalesInvoiceRoutes { get; set; } = null!;
        public DbSet<SalesInvoiceRouteFridgeLine> SalesInvoiceRouteFridgeLines { get; set; } = null!;

        // ===== الموظفون =====
        public DbSet<Department> Departments { get; set; } = null!;
        public DbSet<Job> Jobs { get; set; } = null!;
        public DbSet<Employee> Employees { get; set; } = null!;

        // ===== موديول برنامج المشتريات =====
        public DbSet<VendorProductMapping> VendorProductMappings { get; set; } = null!;
        public DbSet<PurchasePolicyRule> PurchasePolicyRules { get; set; } = null!;
        public DbSet<PurchasingDataSourceConfig> PurchasingDataSourceConfigs { get; set; } = null!;
        public DbSet<VendorFaxUpload> VendorFaxUploads { get; set; } = null!;
        public DbSet<VendorFaxLine> VendorFaxLines { get; set; } = null!;
        public DbSet<PurchasingOrder> PurchasingOrders { get; set; } = null!;
        public DbSet<PurchasingOrderLine> PurchasingOrderLines { get; set; } = null!;
        public DbSet<PurchasingOrderAmendment> PurchasingOrderAmendments { get; set; } = null!;











        private readonly IHttpContextAccessor? _httpContextAccessor;
        private const string DefaultBatchNo = "55555";
        private static readonly DateTime DefaultExpiryDate = new(2028, 1, 1);

        // داخل المُنشئ:
        public AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor httpContextAccessor)
            : base(options)
        {
            _httpContextAccessor = httpContextAccessor; // تعليق: للحصول على اسم المستخدم عند الحفظ
        }







        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);

            mb.Entity<PrintHeaderSetting>(entity =>
            {
                entity.ToTable("PrintHeaderSettings");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.CompanyName).HasMaxLength(200).IsRequired();
                entity.Property(x => x.LogoPath).HasMaxLength(500);
                entity.Property(x => x.UpdatedAt).HasColumnType("datetime2");
            });







            // ===== Stock_Batches (رصيد سريع للتشغيلات) =====
            mb.Entity<StockBatch>(e =>
            {
                e.ToTable("Stock_Batches"); // ✅ الاسم النهائي للجدول

                e.HasKey(x => x.Id);

                // ✅ الصلاحية إلزامية (ممنوع NULL)
                e.Property(x => x.Expiry)
                 .IsRequired();

                // ✅ صف واحد فقط لكل (Warehouse + Prod + BatchNo + Expiry)
                e.HasIndex(x => new { x.WarehouseId, x.ProdId, x.BatchNo, x.Expiry })
                 .IsUnique();

                e.Property(x => x.BatchNo)
                 .HasMaxLength(50)
                 .IsRequired();

                // القيم الافتراضية
                e.Property(x => x.QtyOnHand).HasDefaultValue(0);
                e.Property(x => x.QtyReserved).HasDefaultValue(0);

                // Index سريع للبحث
                e.HasIndex(x => new { x.WarehouseId, x.ProdId });
            });





            mb.Entity<ProductBonusGroup>(entity =>
            {
                entity.ToTable("ProductBonusGroups");          // اسم الجدول في SQL

                entity.HasKey(x => x.ProductBonusGroupId);                      // المفتاح الأساسي

                entity.Property(x => x.Name)
                      .IsRequired()
                      .HasMaxLength(100);

                entity.Property(x => x.BonusAmount)
                      .HasPrecision(10, 2);                    // 12345678.90 مثال

                entity.Property(x => x.CreatedAt)
                      .HasColumnType("datetime2");

                entity.Property(x => x.UpdatedAt)
                      .HasColumnType("datetime2");

            });




            // ===========================
            // جدول التحويلات بين المخازن (الهيدر)
            // ===========================
            mb.Entity<StockTransfer>(entity =>
            {
                entity.ToTable("StockTransfers", t => t.HasCheckConstraint(
                    "CK_StockTransfer_Warehouses",
                    "[FromWarehouseId] <> [ToWarehouseId]"
                ));

                entity.HasKey(t => t.Id);                  // المفتاح الأساسي

                // علاقة: من مخزن (Warehouse -> TransfersFrom -> StockTransfer.FromWarehouse)
                entity.HasOne(t => t.FromWarehouse)
                      .WithMany(w => w.TransfersFrom)      // هنا استخدمنا النفيجاشن اللي في Warehouse
                      .HasForeignKey(t => t.FromWarehouseId)
                      .OnDelete(DeleteBehavior.Restrict);

                // علاقة: إلى مخزن (Warehouse -> TransfersTo -> StockTransfer.ToWarehouse)
                entity.HasOne(t => t.ToWarehouse)
                      .WithMany(w => w.TransfersTo)        // وهنا النفيجاشن التانية
                      .HasForeignKey(t => t.ToWarehouseId)
                      .OnDelete(DeleteBehavior.Restrict);

                // ربط بالمستخدم (اختياري)
                entity.HasOne(t => t.User)
                      .WithMany()
                      .HasForeignKey(t => t.UserId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // ===========================
            // جدول سطور التحويلات (لو مش مضاف)
            // ===========================
            mb.Entity<StockTransferLine>(entity =>
            {
                entity.ToTable("StockTransferLines");

                entity.HasKey(l => l.Id);

                entity.Property(l => l.UnitCost)
                      .HasPrecision(18, 4);

                entity.HasOne(l => l.StockTransfer)
                      .WithMany(t => t.Lines)
                      .HasForeignKey(l => l.StockTransferId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(l => l.Product)
                      .WithMany()
                      .HasForeignKey(l => l.ProductId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(l => l.Batch)
                      .WithMany()
                      .HasForeignKey(l => l.BatchId)
                      .OnDelete(DeleteBehavior.Restrict);
            });




            // ========================
            // إعداد جدول هيدر تسويات المخزون StockAdjustment
            // ========================

            mb.Entity<StockAdjustment>(entity =>
            {
                entity.HasKey(e => e.Id);                               // كود التسوية (PK)

                entity.Property(e => e.AdjustmentDate)
                      .HasColumnType("datetime2");                      // تاريخ التسوية

                entity.Property(e => e.ReferenceNo)
                      .HasMaxLength(50);                                // رقم محضر الجرد (اختياري)

                entity.Property(e => e.Reason)
                      .HasMaxLength(200);                               // سبب التسوية / الملاحظات

                entity.Property(e => e.CreatedAt)
                      .HasColumnType("datetime2");                      // تاريخ الإنشاء

                entity.Property(e => e.UpdatedAt)
                      .HasColumnType("datetime2");                      // آخر تعديل

                // ربط التسوية بالمخزن (WarehouseId FK)
                entity.HasOne(e => e.Warehouse)                         // كل تسوية تخص مخزن واحد
                      .WithMany(w => w.StockAdjustments)                // المخزن عليه قائمة تسويات
                      .HasForeignKey(e => e.WarehouseId)                // FK في جدول التسويات
                      .OnDelete(DeleteBehavior.Restrict);               // لا نحذف المخزن لو عليه تسويات
            });


            // ========================
            // إعداد جدول سطور التسوية StockAdjustmentLine
            // ========================

            mb.Entity<StockAdjustmentLine>(entity =>
            {
                entity.HasKey(e => e.Id);                               // كود السطر

                entity.Property(e => e.Note)
                      .HasMaxLength(200);                               // ملاحظات السطر

                entity.Property(e => e.CostPerUnit)
                      .HasPrecision(18, 4);                             // تكلفة الوحدة

                entity.Property(e => e.PriceRetail)
                      .HasPrecision(18, 2);

                entity.Property(e => e.WeightedDiscountPct)
                      .HasPrecision(5, 2);

                entity.Property(e => e.CostDiff)
                      .HasPrecision(18, 2);                             // فرق التكلفة

                // ربط السطر برأس التسوية
                entity.HasOne(e => e.StockAdjustment)                   // كل سطر ينتمي لتسوية واحدة
                      .WithMany(h => h.Lines)                           // التسوية لها قائمة سطور
                      .HasForeignKey(e => e.StockAdjustmentId)          // FK في جدول السطور
                      .OnDelete(DeleteBehavior.Cascade);                // لو حذفنا التسوية نحذف السطور

                // ربط السطر بالصنف
                entity.HasOne(e => e.Product)                           // كل سطر لصنف واحد
                      .WithMany(p => p.StockAdjustmentLines)            // الصنف له سطور تسوية كثيرة
                      .HasForeignKey(e => e.ProductId)                  // FK في جدول السطور
                      .OnDelete(DeleteBehavior.Restrict);               // لا نحذف الصنف لو عليه تسويات

                // ربط السطر بالتشغيلة (باتش) — اختياري
                entity.HasOne(e => e.Batch)                             // السطر مرتبط بتشغيلة واحدة (لو محددة)
                      .WithMany()                                       // مش محتاج قائمة سطور داخل Batch الآن
                      .HasForeignKey(e => e.BatchId)                    // FK في جدول السطور
                      .OnDelete(DeleteBehavior.Restrict);               // لا نحذف الباتش لو عليه سطور تسوية
            });







            // =======================
            // تكوين جدول السياسات Policy
            // =======================
            mb.Entity<Policy>(entity =>
            {
                entity.ToTable("Policies"); // اسم الجدول في قاعدة البيانات

                // المفتاح الأساسي (احتياطي لو مش متكوّن قبل كده)
                entity.HasKey(p => p.PolicyId);

                // فهرس فريد على اسم السياسة (كل سياسة لها اسم مميز)
                entity.HasIndex(p => p.Name)
                      .IsUnique();

                // (اختياري) توكيد الإعدادات الخاصة بالخصائص
                entity.Property(p => p.Name)
                      .HasMaxLength(100)
                      .IsRequired();

                entity.Property(p => p.Description)
                      .HasMaxLength(500);

                // نسبة الربح الافتراضية للسياسة (لو مفيش قواعد للمخزن أو المجموعة)
                entity.Property(p => p.DefaultProfitPercent)
                      .HasPrecision(5, 2)     // نفس الـ decimal(5,2)
                      .HasDefaultValue(0m);   // الافتراضي = 0%

                // تفعيل افتراضي للسياسة
                entity.Property(p => p.IsActive)
                      .HasDefaultValue(true);


            });







            // =========================================
            // تكوين جدول قواعد السياسات لكل مخزن WarehousePolicyRule
            // =========================================
            mb.Entity<WarehousePolicyRule>(entity =>
            {
                entity.ToTable("WarehousePolicyRules");

                // فهرس فريد على (WarehouseId + PolicyId)
                // علشان ما يبقاش فيه قاعدتين لنفس المخزن ونفس السياسة
                entity.HasIndex(x => new { x.WarehouseId, x.PolicyId })
                      .IsUnique();

                // ربط مع المخزن (Warehouse)
                entity.HasOne(x => x.Warehouse)                      // كل قاعدة تخص مخزن واحد
                      .WithMany()                                   // مش شرط يكون عندنا Navigation في Warehouse دلوقتي
                      .HasForeignKey(x => x.WarehouseId)            // FK على WarehouseId
                      .OnDelete(DeleteBehavior.Restrict);           // منع حذف مخزن لو عليه قواعد سياسة

                // ربط مع السياسة (Policy)
                entity.HasOne(x => x.Policy)
                      .WithMany(p => p.WarehouseRules)              // السياسة لها أكثر من قاعدة في مخازن مختلفة
                      .HasForeignKey(x => x.PolicyId)
                      .OnDelete(DeleteBehavior.Restrict);           // منع حذف سياسة لو مستخدمة
            });

            // ==========================
            // تكوين جدول مجموعات الأصناف ProductGroup
            // ==========================
            mb.Entity<ProductGroup>(entity =>
            {
                entity.ToTable("ProductGroups");

                // فهرس فريد على اسم المجموعة لو تحب
                entity.HasIndex(g => g.Name)
                      .IsUnique();
            });

            // ======================================
            // تكوين جدول سياسة مجموعات الأصناف ProductGroupPolicy
            // ======================================
            mb.Entity<ProductGroupPolicy>(entity =>
            {
                entity.ToTable("ProductGroupPolicies");

                // الفهرس الفريد المركب يسمح بنفس المجموعة والسياسة لمخازن مختلفة
                // (يُعرّف في HasIndex أدناه)

                // ربط مع مجموعة الأصناف
                entity.HasOne(x => x.ProductGroup)
                      .WithMany(g => g.ProductGroupPolicies)    // المجموعة ممكن يكون لها أكثر من سياسة في المستقبل لو عملنا تاريخ
                      .HasForeignKey(x => x.ProductGroupId)
                      .OnDelete(DeleteBehavior.Cascade);        // لو مسحت المجموعة تمسح السياسات التابعة

                // ربط مع السياسة
                entity.HasOne(x => x.Policy)
                      .WithMany(p => p.ProductGroupPolicies)    // السياسة ممكن ترتبط بأكثر من مجموعة
                      .HasForeignKey(x => x.PolicyId)
                      .OnDelete(DeleteBehavior.Restrict);       // منع حذف سياسة لو مستخدمة

                mb.Entity<ProductGroupPolicy>()
                     .HasIndex(x => new { x.ProductGroupId, x.PolicyId, x.WarehouseId })
                     .IsUnique();

                mb.Entity<WarehousePolicyRule>()
                     .HasIndex(x => new { x.PolicyId, x.WarehouseId })
                     .IsUnique();


            });







            // ===== جدول المستخدمين (Users) =====
            mb.Entity<User>(entity =>
            {
                entity.ToTable("Users");                        // اسم الجدول في الداتا بيز

                entity.HasKey(u => u.UserId);                   // المفتاح الأساسي

                // اسم المستخدم يكون إجباري ومميز
                entity.Property(u => u.UserName)
                      .HasMaxLength(50)
                      .IsRequired();

                entity.HasIndex(u => u.UserName)
                      .IsUnique();

                // التاريخ الافتراضي لإنشاء المستخدم
                entity.Property(u => u.CreatedAt)
                      .HasDefaultValueSql("GETUTCDATE()");
            });

            // ===== جدول الأدوار (Roles) =====
            mb.Entity<Role>(entity =>
            {
                entity.ToTable("Roles");

                entity.HasKey(r => r.RoleId);

                entity.Property(r => r.Name)
                      .HasMaxLength(50)
                      .IsRequired();

                entity.HasIndex(r => r.Name)
                      .IsUnique();

                entity.Property(r => r.CreatedAt)
                      .HasDefaultValueSql("GETUTCDATE()");
            });

            // ===== جدول الصلاحيات (Permissions) =====
            mb.Entity<Permission>(entity =>
            {
                entity.ToTable("Permissions");

                entity.HasKey(p => p.PermissionId);

                // كود الصلاحية (مثلاً: SalesInvoices.View) يكون مميز
                entity.Property(p => p.Code)
                      .HasMaxLength(100)
                      .IsRequired();

                entity.HasIndex(p => p.Code)
                      .IsUnique();
            });

            // ===== ربط المستخدمين بالأدوار (UserRoles) =====
            mb.Entity<UserRole>(entity =>
            {
                entity.ToTable("UserRoles");

                // لو في الكلاس عندك Id كمفتاح أساسي سيبه كده:
                entity.HasKey(ur => ur.Id);

                // وربط FK على User
                entity.HasOne(ur => ur.User)
                      .WithMany(u => u.UserRoles)
                      .HasForeignKey(ur => ur.UserId)
                      .OnDelete(DeleteBehavior.Cascade);   // حذف المستخدم يحذف علاقته بالأدوار فقط

                // وربط FK على Role
                entity.HasOne(ur => ur.Role)
                      .WithMany(r => r.UserRoles)
                      .HasForeignKey(ur => ur.RoleId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ===== ربط الأدوار بالصلاحيات (RolePermissions) =====
            mb.Entity<RolePermission>(entity =>
            {
                entity.ToTable("RolePermissions");

                entity.HasKey(rp => rp.Id);

                entity.HasOne(rp => rp.Role)
                      .WithMany(r => r.RolePermissions)
                      .HasForeignKey(rp => rp.RoleId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(rp => rp.Permission)
                      .WithMany(p => p.RolePermissions)
                      .HasForeignKey(rp => rp.PermissionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ===== استثناءات الصلاحيات للمستخدم (UserDeniedPermissions) =====
            mb.Entity<UserDeniedPermission>(entity =>
            {
                entity.ToTable("UserDeniedPermissions");

                entity.HasKey(x => x.Id);

                entity.HasOne(x => x.User)
                      .WithMany(u => u.PermissionOverrides)
                      .HasForeignKey(x => x.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.Permission)
                      .WithMany(p => p.UserDeniedPermissions)
                      .HasForeignKey(x => x.PermissionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ===== إخفاء/إظهار الحسابات للمستخدم (UserAccountVisibilityOverrides) =====
            mb.Entity<UserAccountVisibilityOverride>(entity =>
            {
                entity.ToTable("UserAccountVisibilityOverrides");

                entity.HasKey(x => x.Id);

                entity.HasIndex(x => new { x.UserId, x.AccountId }).IsUnique();

                entity.HasOne(x => x.User)
                      .WithMany()
                      .HasForeignKey(x => x.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.Account)
                      .WithMany()
                      .HasForeignKey(x => x.AccountId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ===== الحسابات المسموح رؤيتها لكل دور (RoleAccountVisibilityOverrides) =====
            mb.Entity<RoleAccountVisibilityOverride>(entity =>
            {
                entity.ToTable("RoleAccountVisibilityOverrides");

                entity.HasKey(x => x.Id);

                entity.HasIndex(x => new { x.RoleId, x.AccountId }).IsUnique();

                entity.HasOne(x => x.Role)
                      .WithMany()
                      .HasForeignKey(x => x.RoleId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.Account)
                      .WithMany()
                      .HasForeignKey(x => x.AccountId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ===== سجل نشاط المستخدمين (UserActivityLogs) =====
            mb.Entity<UserActivityLog>(entity =>
            {
                entity.ToTable("UserActivityLogs");

                entity.HasKey(l => l.Id);

                // التاريخ الافتراضي للحركة
                entity.Property(l => l.ActionTime)
                      .HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(l => l.User)
                      .WithMany(u => u.ActivityLogs)
                      .HasForeignKey(l => l.UserId)
                      .OnDelete(DeleteBehavior.Restrict);   // ما نحذفش اللوج لو المستخدم اتحذف
            });



            // ===== CashReceipt =====
            mb.Entity<CashReceipt>(entity =>
            {
                entity.ToTable("CashReceipts");

                entity.HasKey(e => e.CashReceiptId);

                entity.Property(e => e.ReceiptNumber)
                      .HasMaxLength(50);
                // ✅ ملاحظة: ReceiptNumber لا يكون Required لأنه سيتم توليده تلقائياً من CashReceiptId

                entity.Property(e => e.Description)
                      .HasMaxLength(250);

                entity.Property(e => e.Amount)
                      .HasColumnType("decimal(18,2)");

                entity.HasOne(e => e.CashAccount)
                      .WithMany()
                      .HasForeignKey(e => e.CashAccountId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.CounterAccount)
                      .WithMany()
                      .HasForeignKey(e => e.CounterAccountId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Customer)
                      .WithMany()
                      .HasForeignKey(e => e.CustomerId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ===== CashPayment =====
            mb.Entity<CashPayment>(entity =>
            {
                entity.ToTable("CashPayments");

                entity.HasKey(e => e.CashPaymentId);

                entity.Property(e => e.PaymentNumber)
                      .IsRequired()
                      .HasMaxLength(50);

                entity.Property(e => e.Description)
                      .HasMaxLength(250);

                entity.Property(e => e.Amount)
                      .HasColumnType("decimal(18,2)");

                entity.HasOne(e => e.CashAccount)
                      .WithMany()
                      .HasForeignKey(e => e.CashAccountId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.CounterAccount)
                      .WithMany()
                      .HasForeignKey(e => e.CounterAccountId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Customer)
                      .WithMany()
                      .HasForeignKey(e => e.CustomerId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ===== DebitNote =====
            mb.Entity<DebitNote>(entity =>
            {
                entity.ToTable("DebitNotes");

                entity.HasKey(e => e.DebitNoteId);

                entity.Property(e => e.Reason)
                      .HasMaxLength(100);

                entity.Property(e => e.Description)
                      .HasMaxLength(250);

                entity.Property(e => e.Amount)
                      .HasColumnType("decimal(18,2)");

                entity.HasOne(e => e.Account)
                      .WithMany()
                      .HasForeignKey(e => e.AccountId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.OffsetAccount)
                      .WithMany()
                      .HasForeignKey(e => e.OffsetAccountId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Customer)
                      .WithMany()
                      .HasForeignKey(e => e.CustomerId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // ===== CreditNote =====
            mb.Entity<CreditNote>(entity =>
            {
                entity.ToTable("CreditNotes");

                entity.HasKey(e => e.CreditNoteId);

                entity.Property(e => e.Reason)
                      .HasMaxLength(100);

                entity.Property(e => e.Description)
                      .HasMaxLength(250);

                entity.Property(e => e.Amount)
                      .HasColumnType("decimal(18,2)");

                entity.HasOne(e => e.Account)
                      .WithMany()
                      .HasForeignKey(e => e.AccountId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.OffsetAccount)
                      .WithMany()
                      .HasForeignKey(e => e.OffsetAccountId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Customer)
                      .WithMany()
                      .HasForeignKey(e => e.CustomerId)
                      .OnDelete(DeleteBehavior.Restrict);
            });





            // ===== LedgerEntries (دفتر الأستاذ العام) =====
            mb.Entity<LedgerEntry>(entity =>
            {
                entity.ToTable("LedgerEntries"); // اسم الجدول في قاعدة البيانات

                // المفتاح الأساسي
                entity.HasKey(e => e.Id);

                // التاريخ الأساسي للقيد
                entity.Property(e => e.EntryDate)
                      .IsRequired(); // تاريخ القيد لازم يكون موجود

                // نوع المستند (enum) - مطلوب
                entity.Property(e => e.SourceType)
                      .IsRequired();

                // رقم المستند (اختياري)
                entity.Property(e => e.VoucherNo)
                      .HasMaxLength(50);

                // البيان (اختياري)
                entity.Property(e => e.Description)
                      .HasMaxLength(250);

                // المبالغ (مدين / دائن) بدقة 18,2
                entity.Property(e => e.Debit)
                      .HasColumnType("decimal(18,2)");

                entity.Property(e => e.Credit)
                      .HasColumnType("decimal(18,2)");

                // حقول التتبع الزمني
                entity.Property(e => e.CreatedAt)
                      .HasDefaultValueSql("GETDATE()"); // تاريخ الإنشاء الافتراضي من SQL Server

                entity.Property(e => e.UpdatedAt)
                      .IsRequired(false);              // آخر تعديل (اختياري)

                // العلاقة مع الحساب (Account) - إجباري
                entity.HasOne(e => e.Account)
                      .WithMany()                     // حالياً لا نعرّف Navigation في Account
                      .HasForeignKey(e => e.AccountId)
                      .OnDelete(DeleteBehavior.Restrict);

                // العلاقة مع العميل / الطرف (Customer) - اختيارية
                entity.HasOne(e => e.Customer)
                      .WithMany()                     // لا نحتاج Navigation عكسية الآن
                      .HasForeignKey(e => e.CustomerId)
                      .OnDelete(DeleteBehavior.Restrict);

                // ===== الفهارس لتحسين الأداء في التقارير =====

                // 1) البحث عن قيد مستند معيّن (فاتورة / إيصال...)
                entity.HasIndex(e => new { e.SourceType, e.SourceId });

                // 2) ميزان مراجعة / كشف حساب حسب الحساب والتاريخ
                entity.HasIndex(e => new { e.AccountId, e.EntryDate });

                // 3) كشف حساب عميل / مورد حسب العميل والتاريخ
                entity.HasIndex(e => new { e.CustomerId, e.EntryDate });
            });





            mb.Entity<Account>(entity =>
            {
                // اسم الجدول في قاعدة البيانات
                entity.ToTable("Accounts");

                // المفتاح الأساسي
                entity.HasKey(a => a.AccountId);

                // كود الحساب – مطلوب – طول مناسب
                entity.Property(a => a.AccountCode)
                      .IsRequired()
                      .HasMaxLength(50);

                // اسم الحساب – مطلوب – طول مناسب
                entity.Property(a => a.AccountName)
                      .IsRequired()
                      .HasMaxLength(200);

                // نوع الحساب (enum) يُخزن كعدد صحيح
                entity.Property(a => a.AccountType)
                      .IsRequired();

                // مستوى الحساب
                entity.Property(a => a.Level)
                      .IsRequired();

                // حساب نهائي أم لا
                entity.Property(a => a.IsLeaf)
                      .IsRequired();

                // نشط أم لا
                entity.Property(a => a.IsActive)
                      .IsRequired()
                      .HasDefaultValue(true);

                // ملاحظات (اختيارية)
                entity.Property(a => a.Notes)
                      .HasMaxLength(500);

                // علاقة ذاتية: حساب أب ← حسابات أبناء
                entity.HasOne(a => a.ParentAccount)
                      .WithMany(p => p.Children)
                      .HasForeignKey(a => a.ParentAccountId)
                      .OnDelete(DeleteBehavior.Restrict); // منع حذف الأب لو تحته أبناء

                // فهرس فريد على كود الحساب علشان ما يتكررش
                entity.HasIndex(a => a.AccountCode)
                      .IsUnique();
            });






            // ===== PurchaseReturn =====
            mb.Entity<PurchaseReturn>(e =>
            {
                e.ToTable("PurchaseReturns");

                // 🔹 المفتاح الأساسي
                e.HasKey(x => x.PRetId);

                // 🔹 خصائص أساسية
                e.Property(x => x.PRetId)

                    .IsRequired();

                e.Property(x => x.WarehouseId)

                    .IsRequired();

                e.Property(x => x.CustomerId)

                    .IsRequired();

                e.Property(x => x.Status)
                    .HasMaxLength(20)
                    .HasDefaultValue("Draft");

                // 🔹 علاقة الهيدر مع سطور المرتجع (1..N)
                e.HasMany(x => x.Lines)
                 .WithOne()                         // لا يوجد Navigation في السطر حتى الآن
                 .HasForeignKey(l => l.PRetId)
                 .OnDelete(DeleteBehavior.Cascade); // حذف سطور المرتجع مع الهيدر

                // 🔹 علاقة مرتجع الشراء مع العميل (Customer)
                // مورد واحد -> له العديد من مرتجعات الشراء
                e.HasOne(x => x.Customer)
                 .WithMany(v => v.PurchaseReturns)  // ICollection<PurchaseReturn> داخل Customer
                 .HasForeignKey(x => x.CustomerId)    // FK في الهيدر
                 .OnDelete(DeleteBehavior.Restrict); // لا نحذف المرتجعات تلقائياً عند حذف العميل

                // 🔹 علاقة مرتجع الشراء مع فاتورة الشراء المرجعية (اختياري)
                // RefPIId يمكن أن يكون فارغاً، لذلك العلاقة اختيارية
                e.HasOne(x => x.RefPurchaseInvoice)
                 .WithMany()                        // لو أضفت ICollection<PurchaseReturn> في PurchaseInvoice ممكن تضعها هنا
                 .HasForeignKey(x => x.RefPIId)     // FK في المرتجع
                 .HasPrincipalKey(pi => pi.PIId)    // المفتاح المنطقي في فاتورة الشراء
                 .OnDelete(DeleteBehavior.SetNull); // لو حُذفت الفاتورة، نجعل المرجع NULL ولا نحذف المرتجع
            });


            // ===== PurchaseReturnLine =====
            mb.Entity<PurchaseReturnLine>(e =>
            {
                e.ToTable("PurchaseReturnLines");
                e.HasKey(x => new { x.PRetId, x.LineNo });
                e.Property(x => x.ProdId).IsRequired();
                e.Property(x => x.BatchNo).HasMaxLength(50);

                e.HasIndex(x => x.ProdId);
                e.HasIndex(x => new { x.ProdId, x.BatchNo, x.Expiry });
            });




            // ===== PurchaseRequest =====
            mb.Entity<PurchaseRequest>(e =>
            {
                e.ToTable("PurchaseRequests");

                // ===== المفتاح الأساسي =====
                e.HasKey(x => x.PRId);

                e.Property(x => x.PRId)
                 .ValueGeneratedOnAdd();                 // Identity

                // ===== الخصائص الأساسية =====
                e.Property(x => x.PRDate)
                 .IsRequired();

                e.Property(x => x.NeedByDate);

                e.Property(x => x.WarehouseId)
                 .IsRequired();

                e.Property(x => x.CustomerId)
                 .IsRequired();

                e.Property(x => x.RequestedBy)
                 .HasMaxLength(50)
                 .IsRequired();

                e.Property(x => x.Status)
                 .HasMaxLength(20)
                 .HasDefaultValue("Draft")
                 .IsRequired();

                e.Property(x => x.Notes)
                 .HasMaxLength(500);

                e.Property(x => x.CreatedBy)
                 .HasMaxLength(50)
                 .IsRequired();

                e.Property(x => x.CreatedAt)
                 .IsRequired();

                e.Property(x => x.UpdatedAt);

                // ===== العلاقات =====

                // علاقة 1..N مع سطور طلب الشراء (PRLine) + حذف تتابعي
                e.HasMany(x => x.Lines)
                 .WithOne(l => l.PurchaseRequest)        // لازم تكون موجودة في PRLine
                 .HasForeignKey(l => l.PRId)
                 .OnDelete(DeleteBehavior.Cascade);

                // علاقة طلب الشراء مع العميل (Customer)
                e.HasOne(x => x.Customer)
                 .WithMany(v => v.PurchaseRequests)      // ICollection<PurchaseRequest> داخل Customer
                 .HasForeignKey(x => x.CustomerId)
                 .OnDelete(DeleteBehavior.Restrict);

                // علاقة طلب الشراء مع المخزن (Warehouse)
                e.HasOne(x => x.Warehouse)               // IMPORTANT: استخدم الـ Navigation هنا
                 .WithMany()                             // أو WithMany(w => w.PurchaseRequests) لو عملت كولكشن في Warehouse
                 .HasForeignKey(x => x.WarehouseId)
                 .OnDelete(DeleteBehavior.Restrict);

                // علاقة طلب الشراء مع فواتير الشراء (PurchaseInvoice)
                // طلب واحد ممكن يطلع منه أكثر من فاتورة شراء (في بعض الحالات)
                e.HasMany(x => x.PurchaseInvoices)                 // الكولكشن داخل PurchaseRequest
                 .WithOne(pi => pi.RefPurchaseRequest)             // النفيجيشن داخل PurchaseInvoice
                 .HasForeignKey(pi => pi.RefPRId)                  // IMPORTANT: نحدد إن الـ FK هو RefPRId
                 .OnDelete(DeleteBehavior.SetNull);                // لو تم حذف الطلب، نخلي RefPRId = null في الفواتير

            });






            // ===== PRLine =====


            mb.Entity<PRLine>(e =>
            {
                e.ToTable("PRLines");
                e.HasKey(x => new { x.PRId, x.LineNo });         // مفتاح مركب
                e.Property(x => x.ProdId).IsRequired();
                e.Property(x => x.PriceBasis).HasMaxLength(30);
                e.Property(x => x.PreferredBatchNo).HasMaxLength(50);
                
                // ✅ علاقة PRLine مع Product
                e.HasOne(x => x.Product)
                    .WithMany()
                    .HasForeignKey(x => x.ProdId)
                    .OnDelete(DeleteBehavior.Restrict);

                // فهارس تساعد في التقارير والبحث
                e.HasIndex(x => x.ProdId);
            });



            // ===== PurchaseInvoice =====
            mb.Entity<PurchaseInvoice>(e =>
            {
                e.ToTable("PurchaseInvoices");

                // =========================================================
                // (1) المفتاح الأساسي
                // =========================================================
                e.HasKey(x => x.PIId); // متغير: PK رقم فاتورة المشتريات

                e.Property(x => x.PIId)
                    .ValueGeneratedOnAdd(); // تعليق: Identity (يتولد تلقائيًا)

                // =========================================================
                // (2) الخصائص الأساسية
                // =========================================================
                e.Property(x => x.WarehouseId)
                    .IsRequired(); // متغير: كود المخزن

                e.Property(x => x.CustomerId)
                    .IsRequired(); // متغير: كود المورد

                e.Property(x => x.Status)
                    .HasMaxLength(20)
                    .HasDefaultValue("غير مرحلة"); // متغير: حالة الفاتورة

                e.Property(x => x.RefPRId); // متغير: مرجع طلب شراء (اختياري)

                // =========================================================
                // (3) العلاقة: PurchaseInvoice (Header) -> PILines (Lines)
                // ✅ حذف الفاتورة يمسح سطورها فقط
                // =========================================================
                e.HasMany(x => x.Lines)                    // متغير: سطور الفاتورة
                 .WithOne()                                // تعليق: مفيش Navigation في PILine عندك حاليًا
                 .HasForeignKey(l => l.PIId)               // متغير: FK في السطور
                 .OnDelete(DeleteBehavior.Cascade);        // ✅ Cascade للسطور فقط

                // =========================================================
                // (4) العلاقة مع Customer (المورد)
                // ✅ لا نحذف الفواتير عند حذف المورد
                // =========================================================
                e.HasOne(x => x.Customer)                  // متغير: المورد
                 .WithMany(v => v.PurchaseInvoices)        // متغير: فواتير المورد داخل Customer
                 .HasForeignKey(x => x.CustomerId)
                 .OnDelete(DeleteBehavior.Restrict);       // ✅ Restrict

                // =========================================================
                // (5) العلاقة Warehouse (المخزن)
                // ✅ يجب ربط نفس الـ Navigation (Warehouse) وإلا EF يُنشئ WarehouseId1 ظلاً
                // =========================================================
                e.HasOne(x => x.Warehouse)
                 .WithMany()
                 .HasForeignKey(x => x.WarehouseId)
                 .OnDelete(DeleteBehavior.Restrict);

            });







            // ===== PILine =====
            mb.Entity<PILine>(e =>
            {
                // اسم الجدول في قاعدة البيانات
                e.ToTable("PILines");

                // المفتاح الأساسي المركب: رقم الفاتورة + رقم السطر
                e.HasKey(x => new { x.PIId, x.LineNo });

                // الصنف إلزامي (لازم يكون موجود كرقم)
                e.Property(x => x.ProdId)
                 .IsRequired();

                // طول حقل رقم التشغيلة
                e.Property(x => x.BatchNo)
                 .HasMaxLength(50);

                // ===== ربط سطر فاتورة الشراء برأس الفاتورة (PurchaseInvoice) =====
                e.HasOne(x => x.PurchaseInvoice)      // كل سطر يتبع فاتورة واحدة
                 .WithMany(pi => pi.Lines)           // وكل فاتورة لها مجموعة سطور
                 .HasForeignKey(x => x.PIId)         // المفتاح الأجنبي على السطر هو PIId
                 .OnDelete(DeleteBehavior.Cascade);  // عند حذف الفاتورة تُحذف السطور تلقائيًا

                // ==========================================================
                // ربط سطر فاتورة الشراء بالصنف (Product) عبر ProdId
                // الهدف: نقدر نكتب في Show: line.Product.ProdName
                // ==========================================================
                e.HasOne(x => x.Product)                 // كل سطر مرتبط بصنف واحد
                 .WithMany()                             // (بدون Collection داخل Product حالياً)
                 .HasForeignKey(x => x.ProdId)           // المفتاح الأجنبي: ProdId داخل PILine
                 .OnDelete(DeleteBehavior.Restrict);     // منع حذف صنف عليه سطور شراء

                // ===== الفهارس الشائعة: حسب الصنف أو التشغيلة أو الصلاحية =====
                e.HasIndex(x => x.ProdId);                                // فهرس: البحث بكود الصنف
                e.HasIndex(x => new { x.ProdId, x.BatchNo, x.Expiry });   // فهرس: صنف + باتش + صلاحية
            });







            mb.Entity<ERP.Models.DocumentSeries>(e =>
            {
                e.ToTable("DocumentSeries");

                e.HasKey(x => x.SeriesId);

                e.Property(x => x.DocType)
                    .HasMaxLength(30)
                    .IsRequired();

                e.Property(x => x.SeriesCode)
                    .HasMaxLength(20)
                    .IsRequired();

                e.Property(x => x.FiscalYear)
                    .HasMaxLength(4);

                e.Property(x => x.ResetPolicy)
                    .HasMaxLength(15)
                    .IsRequired()
                    .HasDefaultValue("Continuous"); // سياسة افتراضية

                e.Property(x => x.CurrentNo)
                    .IsRequired()
                    .HasDefaultValue(0);

                e.Property(x => x.NumberWidth)
                    .IsRequired()
                    .HasDefaultValue(6);

                e.Property(x => x.Prefix)
                    .HasMaxLength(20);

                e.Property(x => x.CreatedAt)
                    .HasDefaultValueSql("SYSDATETIME()");

                e.Property(x => x.UpdatedAt);

                e.Property(x => x.RowVersion)
                    .IsRowVersion(); // rowversion تلقائي

                // فهرس فريد لضمان صف واحد لكل (نوع + سلسلة + سنة)
                e.HasIndex(x => new { x.DocType, x.SeriesCode, x.FiscalYear })
                 .IsUnique()
                 .HasDatabaseName("UX_DocumentSeries_DocType_Series_Year");
            });






            mb.Entity<Governorate>(e =>
            {
                e.ToTable("Governorates");
                e.HasKey(x => x.GovernorateId);

                e.Property(x => x.GovernorateId)

                 .IsRequired();

                e.Property(x => x.GovernorateName)
                 .HasMaxLength(100)
                 .IsRequired();

                // فهرس فريد على اسم المحافظة
                e.HasIndex(x => x.GovernorateName)
                 .IsUnique()
                 .HasDatabaseName("UX_Governorates_Name");
            });








            mb.Entity<ERP.Models.City>(entity =>
            {
                entity.ToTable("Cities");

                // المفاتيح والأنواع
                entity.HasKey(e => e.CityId);
                entity.Property(e => e.CityName)
                      .IsRequired()
                      .HasMaxLength(150);

                entity.Property(e => e.Notes)
                  .HasMaxLength(250);

                // القيم الافتراضية والتواريخ
                entity.Property(e => e.IsActive)
                  .HasDefaultValue(true);

                entity.Property(e => e.CreatedAt)
                  .HasDefaultValueSql("GETDATE()"); // تاريخ الإنشاء تلقائيًا

                entity.Property(e => e.UpdatedAt)
                  .HasDefaultValueSql("NULL"); // يبدأ Null ويتحدث عند التعديل يدويًا

                // العلاقة مع المحافظات (FK) مع RESTRICT عند الحذف
                entity.HasOne(e => e.Governorate)
                  .WithMany(g => g.Cities)
                  .HasForeignKey(e => e.GovernorateId)
                  .OnDelete(DeleteBehavior.Restrict);

                // فهرس فريد لمنع تكرار نفس الاسم داخل نفس المحافظة
                entity.HasIndex(e => new
                {
                    e.GovernorateId,
                    e.CityName
                })
                  .IsUnique()
                  .HasDatabaseName("UX_Cities_GovernorateId_CityName");

                // فهرس لتسريع الفلترة حسب المحافظة
                entity.HasIndex(e => e.GovernorateId)
                      .HasDatabaseName("IX_Cities_GovernorateId");
            });







            mb.Entity<ERP.Models.District>(e =>
            {
                e.ToTable("Districts");                         // اسم الجدول
                e.HasKey(x => x.DistrictId);                    // المفتاح الأساسي
                e.Property(x => x.DistrictId).ValueGeneratedOnAdd();

                // الأعمدة
                e.Property(x => x.DistrictName).HasMaxLength(150).IsRequired();
                e.Property(x => x.Notes).HasMaxLength(250).IsRequired(false);
                e.Property(x => x.IsActive).HasDefaultValue(true);

                // الفهارس والقيود
                e.HasIndex(x => new { x.GovernorateId, x.DistrictName })
                 .IsUnique()
                 .HasDatabaseName("UX_Districts_Gov_Name");     // يمنع تكرار نفس الاسم داخل نفس المحافظة

                e.HasIndex(x => x.GovernorateId);

                // العلاقة مع المحافظات — RESTRICT عند الحذف
                e.HasOne(x => x.Governorate)
                 .WithMany(g => g.Districts)
                 .HasForeignKey(x => x.GovernorateId)
                 .OnDelete(DeleteBehavior.Restrict);
            });






            // ===== Areas =====

            mb.Entity<Area>(entity =>
            {
                entity.ToTable("Areas");

                entity.HasKey(e => e.AreaId);

                entity.Property(e => e.AreaName)
                      .IsRequired()
                      .HasMaxLength(150);

                entity.Property(e => e.Notes)
                      .HasMaxLength(250);

                entity.Property(e => e.IsActive)
                      .HasDefaultValue(true);

                // FK: المحافظة
                entity.HasOne(e => e.Governorate)
                      .WithMany(g => g.Areas)
                      .HasForeignKey(e => e.GovernorateId)
                      .OnDelete(DeleteBehavior.Restrict);

                // FK: الحي/المركز (اختياري — «لا يوجد حي/مركز»)
                entity.HasOne(e => e.District)
                      .WithMany(d => d.Areas)
                      .HasForeignKey(e => e.DistrictId)
                      .IsRequired(false)
                      .OnDelete(DeleteBehavior.Restrict);

                // منع التكرار: نفس الاسم داخل نفس الحي (والمناطق دون حي يُسمح بتكرار الاسم)
                entity.HasIndex(e => new { e.DistrictId, e.AreaName })
                      .IsUnique()
                      .HasDatabaseName("UX_Areas_District_Name");

                // فهارس للتسريع
                entity.HasIndex(e => e.GovernorateId).HasDatabaseName("IX_Areas_GovernorateId");
                entity.HasIndex(e => e.DistrictId).HasDatabaseName("IX_Areas_DistrictId");
            });






            mb.Entity<Customer>(e =>
            {
                e.ToTable("Customers");
                e.HasKey(x => x.CustomerId);

                e.Property(x => x.CustomerName).IsRequired().HasMaxLength(200);
                e.Property(x => x.Phone1).HasMaxLength(20);
                e.Property(x => x.Phone2).HasMaxLength(20);
                e.Property(x => x.Whatsapp).HasMaxLength(20);
                e.Property(x => x.Address).HasMaxLength(250);
                e.Property(x => x.Notes).HasMaxLength(300);

                // ضبط الديسيمل (الحد الائتماني/الرصيد)
                e.Property(x => x.CreditLimit).HasColumnType("decimal(18,2)");
                e.Property(x => x.CurrentBalance).HasColumnType("decimal(18,2)");

                // فهارس
                e.HasIndex(x => x.CustomerName).HasDatabaseName("IX_Customers_Name");
                e.HasIndex(x => x.Phone1).HasDatabaseName("IX_Customers_Phone1");

                // قيد فريد داخل نفس النطاق الجغرافي (اختياري — مناسب لعملاء البيع بالتجزئة)
                e.HasIndex(x => new { x.GovernorateId, x.DistrictId, x.AreaId, x.CustomerName })
                 .IsUnique()
                 .HasDatabaseName("UX_Customers_UniqueNameInArea");

                // علاقات بدون Cascade Delete
                e.HasOne(x => x.Governorate).WithMany()
                 .HasForeignKey(x => x.GovernorateId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.District).WithMany()
                 .HasForeignKey(x => x.DistrictId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Area).WithMany()
                 .HasForeignKey(x => x.AreaId)
                 .OnDelete(DeleteBehavior.Restrict);

                // ===== سياسة العميل =====

                e.HasOne(c => c.Policy)                 // العميل له سياسة واحدة
                .WithMany()                            // مش محتاجين List<Customer> جوه Policy حالياً
                .HasForeignKey(c => c.PolicyId)        // FK على PolicyId
                .OnDelete(DeleteBehavior.SetNull);     // لو اتلغت السياسة نخلي PolicyId = NULL

                // ===== مسئول الطلب داخل الصيدلية =====
                e.Property(c => c.OrderContactName)
                      .HasMaxLength(200);        // اسم المسئول عن الطلب

                e.Property(c => c.OrderContactPhone)
                      .HasMaxLength(20);         // موبايل المسئول عن الطلب

                // ===== مضاعفة الكوتة =====
                e.Property(c => c.IsQuotaMultiplierEnabled)
                      .HasDefaultValue(false);   // افتراضيًا: لا يضاعف الكوتة

                e.Property(c => c.QuotaMultiplier)
                      .HasDefaultValue(1);       // افتراضيًا: 1 (بدون مضاعفة)

                // ===== المستخدم المسئول عن العميل (السيلز) =====
                e.HasOne(c => c.User)
                      .WithMany()               // لو حبيت بعدين تضيف ICollection<Customer> في User نغيرها
                      .HasForeignKey(c => c.UserId)
                      .OnDelete(DeleteBehavior.SetNull);

                // ===== خط السير =====
                e.HasOne(c => c.Route)
                 .WithMany(r => r.Customers)
                 .HasForeignKey(c => c.RouteId)
                 .OnDelete(DeleteBehavior.SetNull);
            });




            // ===== Products =====


            mb.Entity<Product>(e =>
            {
                // جدول Products + إضافة قيد التحقق على نفس التعريف
                // ملاحظة: لا تستعمل ToTable مرّتين
                e.ToTable("Products", tb =>
                {
                    // قيد التحقق: القيم المسموح بها في Imported
                    tb.HasCheckConstraint(
                        "CK_Products_Imported",
                        "[Imported] IN (N'محلي', N'مستورد', N'غير معروف')");
                });

                // المفتاح الأساسي
                e.HasKey(x => x.ProdId);

                // الخصائص الأساسية (كل خاصية عليها تعليق عربي لشرح الاستخدام)
                e.Property(x => x.ProdId)               // معرف الصنف (PK)

                    .IsUnicode(true)
                    .IsRequired();

                e.Property(x => x.ProdName)             // اسم الصنف
                    .HasMaxLength(200)
                    .IsUnicode(true);

                e.Property(x => x.Barcode)              // الباركود
                    .HasMaxLength(100)
                    .IsUnicode(true);

                e.Property(x => x.GenericName)          // الاسم العلمي
                    .HasMaxLength(200)
                    .IsUnicode(true);

                e.Property(x => x.Strength)             // التركيز
                    .HasMaxLength(100)
                    .IsUnicode(true);

                e.Property(x => x.CategoryId)           // معرف الفئة (FK اختياري)
                    .HasMaxLength(50)
                    .IsUnicode(true);

                e.Property(x => x.PriceRetail)          // سعر الجمهور
                    .HasColumnType("decimal(18,2)");

                e.Property(x => x.Description)          // الوصف
                    .IsUnicode(true);

                e.Property(x => x.DosageForm)           // الشكل الدوائي
                    .IsUnicode(true);

                e.Property(x => x.Imported)             // محلي/مستورد/غير معروف
                    .HasMaxLength(20)
                    .IsUnicode(true)
                    .HasDefaultValue("غير معروف");      // القيمة الافتراضية

                e.Property(x => x.Company)              // الشركة
                    .IsUnicode(true);

                e.Property(x => x.Location)             // الموقع
                    .HasMaxLength(50)
                    .IsUnicode(true);

                e.Property(x => x.ExternalCode)        // كود الإكسل
                    .HasMaxLength(50)
                    .IsUnicode(true);

                e.Property(x => x.WarehouseId);         // المخزن الافتراضي

                e.Property(x => x.IsActive);            // فعال

                e.Property(x => x.LastPriceChangeDate); // تاريخ آخر تغيير للسعر

                e.Property(x => x.CartonQuantity);      // كمية الوحدات في الكرتونة (اختياري)
                e.Property(x => x.PackQuantity);        // كمية الوحدات في الباكو (اختياري)

                // الربط مع الفئات (FK على CategoryId) — حذف الفئة لا يحذف الأصناف
                e.HasOne(x => x.Category)
                 .WithMany(c => c.Products)
                 .HasForeignKey(x => x.CategoryId)
                 .HasConstraintName("FK_Products_Categories_CategoryId")
                 .OnDelete(DeleteBehavior.SetNull);

                e.HasOne(p => p.ProductGroup)                 // الصنف ينتمي لمجموعة (اختياري)
                .WithMany(g => g.Products)                   // المجموعة تحتوي على عدة أصناف
                .HasForeignKey(p => p.ProductGroupId)        // FK على ProductGroupId
                 .OnDelete(DeleteBehavior.SetNull);           // لو مسحنا المجموعة نخلي المجموعة = NULL في الأصناف

                e.HasOne(p => p.Classification)
                 .WithMany(c => c.Products)
                 .HasForeignKey(p => p.ClassificationId)
                 .OnDelete(DeleteBehavior.SetNull);

                e.HasOne(p => p.Warehouse)
                 .WithMany()
                 .HasForeignKey(p => p.WarehouseId)
                 .OnDelete(DeleteBehavior.SetNull);
            });


            // ===== Branches =====
            mb.Entity<Branch>(entity =>
            {
                entity.ToTable("Branches");               // اسم الجدول في القاعدة
                entity.HasKey(e => e.BranchId);           // المفتاح الأساسي (string)

                entity.Property(e => e.BranchId)

                      .IsRequired();

                entity.Property(e => e.BranchName)
                      .HasMaxLength(200)
                      .IsRequired();
            });




            // ===== Warehouses (المخازن) =====

            mb.Entity<ERP.Models.Warehouse>(mb =>
            {
                mb.ToTable("Warehouses");                          // اسم الجدول في SQL
                mb.HasKey(w => w.WarehouseId);                     // PK

                mb.Property(w => w.WarehouseId)

                  .IsRequired();

                mb.Property(w => w.WarehouseName)
                  .HasMaxLength(200)
                  .IsRequired();

                mb.Property(w => w.BranchId)

                  .IsRequired();

                mb.Property(w => w.IsActive)
                  .HasDefaultValue(true);

                mb.Property(w => w.Notes)
                  .HasMaxLength(500);

                // FK: Warehouse.BranchId → Branch.BranchId
                mb.HasOne(w => w.Branch)
                  .WithMany(b => b.Warehouses)
                  .HasForeignKey(w => w.BranchId)
                  .OnDelete(DeleteBehavior.Restrict); // منع الحذف التتبعي للفرع



            });



            // ===== Categories =====

            mb.Entity<Category>(e =>
            {
                e.ToTable("Categories");
                e.HasKey(x => x.CategoryId);

                e.Property(x => x.CategoryId)
                 // مهم: الطول يُطابق FK في Products
                 .IsUnicode(true)
                 .IsRequired();

                e.Property(x => x.CategoryName)
                 .HasMaxLength(200)
                 .IsUnicode(true)
                 .IsRequired();
            });

            // ===== ProductClassifications (تصنيفات الأصناف — عادي، ثلاجة، …) =====
            mb.Entity<ProductClassification>(e =>
            {
                e.ToTable("ProductClassifications");
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.Property(x => x.Code).HasMaxLength(20);
            });

            // ===== Routes (خطوط السير) =====
            mb.Entity<ERP.Models.Route>(e =>
            {
                e.ToTable("Routes");
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.Property(x => x.Code).HasMaxLength(20);
            });

            // ===== SalesInvoiceRoute (بيانات خط السير لكل فاتورة) — العلاقة واحد-لواحد مع SalesInvoice مُعرّفة في SalesInvoice =====
            mb.Entity<SalesInvoiceRoute>(e =>
            {
                e.ToTable("SalesInvoiceRoutes");
                e.HasKey(x => x.SIId);
                e.Property(x => x.Notes).HasMaxLength(500);
                // NO ACTION لتجنب multiple cascade paths في SQL Server (حذف الموظف لا يغيّر القيد؛ يُفضّل التعامل في الكود إن لزم)
                e.HasOne(x => x.ControlEmployee).WithMany().HasForeignKey(x => x.ControlEmployeeId).OnDelete(DeleteBehavior.NoAction);
                e.HasOne(x => x.PreparerEmployee).WithMany().HasForeignKey(x => x.PreparerEmployeeId).OnDelete(DeleteBehavior.NoAction);
                e.HasOne(x => x.DistributorEmployee).WithMany().HasForeignKey(x => x.DistributorEmployeeId).OnDelete(DeleteBehavior.NoAction);
                e.HasMany(x => x.FridgeLines).WithOne(l => l.Route).HasForeignKey(l => l.SIId).OnDelete(DeleteBehavior.Cascade);
            });

            mb.Entity<SalesInvoiceRouteFridgeLine>(e =>
            {
                e.ToTable("SalesInvoiceRouteFridgeLines");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            });

            // ===== Employees (الموظفون) =====
            mb.Entity<Department>(e =>
            {
                e.ToTable("Departments");
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.Property(x => x.Code).HasMaxLength(20);
            });

            mb.Entity<Job>(e =>
            {
                e.ToTable("Jobs");
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.Property(x => x.Code).HasMaxLength(20);
            });

            mb.Entity<Employee>(e =>
            {
                e.ToTable("Employees");
                e.HasKey(x => x.Id);
                e.Property(x => x.FullName).HasMaxLength(100).IsRequired();
                e.Property(x => x.Code).HasMaxLength(20);
                e.Property(x => x.NationalId).HasMaxLength(20);
                e.Property(x => x.Phone1).HasMaxLength(20);
                e.Property(x => x.Phone2).HasMaxLength(20);
                e.Property(x => x.Email).HasMaxLength(100);
                e.Property(x => x.Address).HasMaxLength(300);
                e.Property(x => x.Notes).HasMaxLength(500);
                e.HasOne(x => x.User)
                 .WithMany()
                 .HasForeignKey(x => x.UserId)
                 .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(x => x.Department)
                 .WithMany(d => d.Employees)
                 .HasForeignKey(x => x.DepartmentId)
                 .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(x => x.Job)
                 .WithMany(j => j.Employees)
                 .HasForeignKey(x => x.JobId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            // ===== price exchange table =====

            mb.Entity<ProductPriceHistory>(e =>
            {
                e.ToTable("ProductPriceHistory");
                e.HasKey(x => x.PriceChangeId);
                e.Property(x => x.PriceChangeId).ValueGeneratedOnAdd();

                e.Property(x => x.ProdId).IsUnicode(true).IsRequired();
                e.Property(x => x.OldPrice).HasColumnType("decimal(18,2)").IsRequired();
                e.Property(x => x.NewPrice).HasColumnType("decimal(18,2)").IsRequired();
                e.Property(x => x.ChangeDate).HasColumnType("datetime2").HasDefaultValueSql("GETUTCDATE()");
                e.Property(x => x.ChangedBy).HasMaxLength(100).IsUnicode(true);
                e.Property(x => x.Reason).HasMaxLength(250).IsUnicode(true);

                // FK باستخدام الملاحة
                e.HasOne(x => x.Product)
                 .WithMany()
                 .HasForeignKey(x => x.ProdId)
                 .HasConstraintName("FK_PriceHistory_Products_ProdId")
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(x => new { x.ProdId, x.ChangeDate });
            });



            // ===== SalesInvoices Headers =====

            mb.Entity<SalesInvoice>(e =>
            {
                // جدول باسم ASCII فقط
                e.ToTable("SalesInvoices", tb =>
                {
                    // قيود التحقق بأسماء أعمدة ASCII (العربي هنا فقط كقيم نصية N'...')
                    tb.HasCheckConstraint(
                        "CK_SalesInvoices_Status",
                        "[Status] IN (N'مسودة', N'مرحل', N'ملغى')");

                    tb.HasCheckConstraint(
                        "CK_SalesInvoices_PaymentMethod",
                        "[PaymentMethod] IS NULL OR [PaymentMethod] IN (N'نقدي', N'شبكة', N'آجل', N'مختلط')");

                    tb.HasCheckConstraint(
                        "CK_SalesInvoices_DiscountPercent",
                        "[HeaderDiscountPercent] >= 0 AND [HeaderDiscountPercent] <= 100");
                });

                // المفتاح
                e.HasKey(x => x.SIId).HasName("PK_SalesInvoices");

                // خصائص بدون HasColumnName (EF سيستخدم أسماء الخصائص كأسماء أعمدة ASCII)
                e.Property(x => x.SIId).IsRequired();
                e.Property(x => x.SeriesCode).HasMaxLength(10);
                e.Property(x => x.FiscalYear).HasMaxLength(4).IsFixedLength();

                e.Property(x => x.SIDate).HasColumnType("date").IsRequired();
                e.Property(x => x.SITime).HasColumnType("time(0)")
                                          .HasDefaultValueSql("CONVERT(time(0), SYSDATETIME())")
                                          .IsRequired();

                e.Property(x => x.CustomerId).IsRequired();
                e.Property(x => x.WarehouseId).IsRequired();
                e.Property(x => x.PaymentMethod).HasMaxLength(20);

                e.Property(x => x.HeaderDiscountPercent).HasPrecision(5, 2).HasDefaultValue(0m).IsRequired();
                e.Property(x => x.HeaderDiscountValue).HasPrecision(18, 2).HasDefaultValue(0m).IsRequired();
                e.Property(x => x.TotalBeforeDiscount).HasPrecision(18, 2).HasDefaultValue(0m).IsRequired();
                e.Property(x => x.TotalAfterDiscountBeforeTax).HasPrecision(18, 2).HasDefaultValue(0m).IsRequired();
                e.Property(x => x.TaxAmount).HasPrecision(18, 2).HasDefaultValue(0m).IsRequired();
                e.Property(x => x.NetTotal).HasPrecision(18, 2).HasDefaultValue(0m).IsRequired();

                e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("مسودة").IsRequired();
                e.Property(x => x.IsPosted).HasDefaultValue(false).IsRequired();
                e.Property(x => x.PostedAt).HasColumnType("datetime2(0)");
                e.Property(x => x.PostedBy).HasMaxLength(50);

                e.Property(x => x.CreatedBy).HasMaxLength(50).IsRequired();
                e.Property(x => x.CreatedAt).HasColumnType("datetime2(0)")
                                            .HasDefaultValueSql("SYSDATETIME()")
                                            .IsRequired();
                e.Property(x => x.UpdatedAt).HasColumnType("datetime2(0)");

                e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
                e.Property(x => x.RefSOId);  // أمر البيع المصدر (اختياري)

                // فهارس
                e.HasIndex(x => new { x.SIDate, x.SIId }).HasDatabaseName("IX_SalesInvoices_Date_Id");
                e.HasIndex(x => new { x.CustomerId, x.SIDate }).HasDatabaseName("IX_SalesInvoices_Customer_Date");
                e.HasIndex(x => new { x.WarehouseId, x.SIDate }).HasDatabaseName("IX_SalesInvoices_Warehouse_Date");

                // علاقة هيدر ← سطور (حذف الفاتورة يمسح السطور)
                e.HasMany(x => x.Lines)
                 .WithOne(l => l.SalesInvoice)   // هنا استخدمنا الـ Navigation اللي أضفناه في SalesInvoiceLine
                 .HasForeignKey(l => l.SIId)     // FK في جدول السطور
                 .OnDelete(DeleteBehavior.Cascade);


                // 🔹 علاقة فاتورة البيع مع العميل (Customer)
                e.HasOne(x => x.Customer)
                 .WithMany(c => c.SalesInvoices)
                 .HasForeignKey(x => x.CustomerId)
                 .OnDelete(DeleteBehavior.Restrict);

                // 🔹 علاقة فاتورة البيع مع المخزن (Warehouse)
                e.HasOne(x => x.Warehouse)
                 .WithMany()
                 .HasForeignKey(x => x.WarehouseId)
                 .OnDelete(DeleteBehavior.Restrict);

                // 🔹 علاقة اختيارية مع أمر البيع المصدر
                e.HasOne(x => x.SalesOrder)
                 .WithMany()
                 .HasForeignKey(x => x.RefSOId)
                 .OnDelete(DeleteBehavior.Restrict);

                // 🔹 بيانات خط السير (واحد لواحد)
                e.HasOne(x => x.Route)
                 .WithOne(r => r.SalesInvoice)
                 .HasForeignKey<SalesInvoiceRoute>(r => r.SIId)
                 .OnDelete(DeleteBehavior.Cascade);
            });





            // ===== SalesInvoiceLines (ASCII names) =====


            mb.Entity<SalesInvoiceLine>(e =>
            {
                // ===== اسم الجدول + قيود التحقق على مستوى الجدول =====
                e.ToTable("SalesInvoiceLines", tb =>
                {
                    // الكمية لازم تكون > 0
                    tb.HasCheckConstraint("CK_SIL_Qty_Positive", "[Qty] > 0");

                    // نسب الخصم الثلاثة بين 0 و 100
                    tb.HasCheckConstraint("CK_SIL_Disc1",
                        "[Disc1Percent] >= 0 AND [Disc1Percent] <= 100");
                    tb.HasCheckConstraint("CK_SIL_Disc2",
                        "[Disc2Percent] >= 0 AND [Disc2Percent] <= 100");
                    tb.HasCheckConstraint("CK_SIL_Disc3",
                        "[Disc3Percent] >= 0 AND [Disc3Percent] <= 100");

                    // نسبة الضريبة بين 0 و 100
                    tb.HasCheckConstraint("CK_SIL_Tax",
                        "[TaxPercent] >= 0 AND [TaxPercent] <= 100");
                });

                // ===== المفتاح المركّب (رقم الفاتورة + رقم السطر) =====
                e.HasKey(x => new { x.SIId, x.LineNo });    // كل سطر مميَّز داخل فاتورة معينة

                // ===== أطوال الحقول النصية =====
                e.Property(x => x.SIId)

                 .IsRequired();                             // رقم الفاتورة إجباري

                e.Property(x => x.ProdId)

                 .IsRequired();                             // كود الصنف إجباري

                e.Property(x => x.BatchNo)
                 .HasMaxLength(50);                         // رقم التشغيلة (اختياري)

                e.Property(x => x.Notes)
                 .HasMaxLength(250);                        // ملاحظات السطر (اختياري)

                // ===== دقة الأرقام المالية والنِّسب =====
                e.Property(x => x.PriceRetail)
                 .HasPrecision(18, 2)
                 .IsRequired();                             // سعر الجمهور

                e.Property(x => x.Disc1Percent)
                 .HasPrecision(5, 2)
                 .HasDefaultValue(0m);                      // خصم 1 %

                e.Property(x => x.Disc2Percent)
                 .HasPrecision(5, 2)
                 .HasDefaultValue(0m);                      // خصم 2 %

                e.Property(x => x.Disc3Percent)
                 .HasPrecision(5, 2)
                 .HasDefaultValue(0m);                      // خصم 3 %

                e.Property(x => x.DiscountValue)
                 .HasPrecision(18, 2)
                 .HasDefaultValue(0m);                      // خصم بالقيمة

                e.Property(x => x.UnitSalePrice)
                 .HasPrecision(18, 2)
                 .IsRequired();                             // سعر البيع للوحدة بعد الخصم

                e.Property(x => x.LineTotalAfterDiscount)
                 .HasPrecision(18, 2)
                 .HasDefaultValue(0m);                      // الإجمالي قبل الضريبة

                e.Property(x => x.TaxPercent)
                 .HasPrecision(5, 2)
                 .HasDefaultValue(0m);                      // نسبة الضريبة

                e.Property(x => x.TaxValue)
                 .HasPrecision(18, 2)
                 .HasDefaultValue(0m);                      // قيمة الضريبة

                e.Property(x => x.LineNetTotal)
                 .HasPrecision(18, 2)
                 .HasDefaultValue(0m);                      // صافي السطر بعد الضريبة

                // ===== فهارس لتحسين البحث والتقارير =====
                e.HasIndex(x => x.SIId)
                 .HasDatabaseName("IX_SIL_SIId");           // البحث / التجميع حسب الفاتورة

                e.HasIndex(x => x.ProdId)
                 .HasDatabaseName("IX_SIL_ProdId");      // تقارير حسب الصنف

                // ===== علاقة السطر مع رأس الفاتورة =====
                e.HasOne(x => x.SalesInvoice)               // كل سطر ينتمي إلى فاتورة واحدة
                 .WithMany(h => h.Lines)                    // والفاتورة لها مجموعة سطور
                 .HasForeignKey(x => x.SIId)                // الـ FK في جدول السطور
                 .OnDelete(DeleteBehavior.Cascade);         // حذف الفاتورة يمسح سطورها
            });




            // ===== SalesReturns (هيدر مرتجع البيع) =====
            mb.Entity<SalesReturn>(e =>
            {
                e.ToTable("SalesReturns");
                e.HasKey(x => x.SRId);

                e.Property(x => x.SRId).IsRequired();
                e.Property(x => x.SRDate)
                 .HasColumnType("date")
                 .IsRequired();

                e.Property(x => x.SRTime)
                 .HasColumnType("time(0)")
                 .HasDefaultValueSql("CONVERT(time(0), SYSDATETIME())");

                e.Property(x => x.CustomerId).IsRequired();
                e.Property(x => x.WarehouseId).IsRequired();

                e.Property(x => x.HeaderDiscountPercent).HasColumnType("decimal(5,2)").HasDefaultValue(0m);
                e.Property(x => x.HeaderDiscountValue).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
                e.Property(x => x.TotalBeforeDiscount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
                e.Property(x => x.TotalAfterDiscountBeforeTax).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
                e.Property(x => x.TaxAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
                e.Property(x => x.NetTotal).HasColumnType("decimal(18,2)").HasDefaultValue(0m);

                e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("Draft");
                e.Property(x => x.IsPosted).HasDefaultValue(false);
                e.Property(x => x.PostedBy).HasMaxLength(50);

                e.Property(x => x.CreatedBy).HasMaxLength(50).IsRequired();
                e.Property(x => x.CreatedAt)
                 .HasColumnType("datetime2(0)")
                 .HasDefaultValueSql("SYSDATETIME()");
                e.Property(x => x.UpdatedAt).HasColumnType("datetime2(0)");

                e.Property(x => x.RowVersion)
                 .IsRowVersion()
                 .IsConcurrencyToken();

                // فهارس
                e.HasIndex(x => x.SRDate);
                e.HasIndex(x => new { x.CustomerId, x.SRDate });
                e.HasIndex(x => new { x.WarehouseId, x.SRDate });

                // هيدر واحد -> سطور كثيرة (علاقة واحدة بس بكاسكيد)
                e.HasMany(x => x.Lines)
                 .WithOne(l => l.SalesReturn)      // نربط الـ Navigation اللى فى SalesReturnLine
                 .HasForeignKey(l => l.SRId)
                 .OnDelete(DeleteBehavior.Cascade);

                // الهيدر ↔ العميل
                e.HasOne(x => x.Customer)
                 .WithMany(c => c.SalesReturns)
                 .HasForeignKey(x => x.CustomerId)
                 .OnDelete(DeleteBehavior.Restrict);

                // قيود تحقق
                e.ToTable(tb =>
                {
                    tb.HasCheckConstraint("CK_SalesReturns_DiscountPercent",
                        "[HeaderDiscountPercent] >= 0 AND [HeaderDiscountPercent] <= 100");
                    tb.HasCheckConstraint("CK_SalesReturns_Status",
                        "[Status] IN ('Draft','Posted','Cancelled')");
                });
            });




            // ===== SalesReturnLines (سطور مرتجع البيع) =====
            mb.Entity<SalesReturnLine>(e =>
            {
                e.ToTable("SalesReturnLines");

                // المفتاح المركّب: رقم المرتجع + رقم السطر
                e.HasKey(x => new { x.SRId, x.LineNo });

                e.Property(x => x.SRId).IsRequired();
                e.Property(x => x.LineNo).IsRequired();
                e.Property(x => x.ProdId).IsRequired();
                e.Property(x => x.Qty).IsRequired();

                e.Property(x => x.BatchNo).HasMaxLength(50);

                // أسعار وخصومات وضريبة
                e.Property(x => x.PriceRetail).HasColumnType("decimal(18,2)");
                e.Property(x => x.Disc1Percent).HasColumnType("decimal(5,2)");
                e.Property(x => x.Disc2Percent).HasColumnType("decimal(5,2)");
                e.Property(x => x.Disc3Percent).HasColumnType("decimal(5,2)");
                e.Property(x => x.DiscountValue).HasColumnType("decimal(18,2)");
                e.Property(x => x.TaxPercent).HasColumnType("decimal(5,2)");

                e.Property(x => x.UnitSalePrice).HasColumnType("decimal(18,2)");
                e.Property(x => x.LineTotalAfterDiscount).HasColumnType("decimal(18,2)");
                e.Property(x => x.TaxValue).HasColumnType("decimal(18,2)");
                e.Property(x => x.LineNetTotal).HasColumnType("decimal(18,2)");

                // فهرس على الصنف
                e.HasIndex(x => x.ProdId);

                // ===== العلاقات =====

                // ١) السطر ↔ هيدر المرتجع (اللى فوق ظبطنا الناحية التانية)
                e.HasOne(l => l.SalesReturn)          // Navigation فى SalesReturnLine
                 .WithMany(h => h.Lines)             // ICollection<SalesReturnLine> فى SalesReturn
                 .HasForeignKey(l => l.SRId)
                 .OnDelete(DeleteBehavior.Cascade);

                // ٢) ربط اختيارى مع هيدر فاتورة البيع
                e.HasOne(l => l.SalesInvoice)        // SalesReturnLine.SalesInvoice
                 .WithMany()                         // لسه ما عملناش ICollection<SalesReturnLine> فى SalesInvoice
                 .HasForeignKey(l => l.SalesInvoiceId)
                 .OnDelete(DeleteBehavior.Restrict); // بدون Cascade علشان ما يعملش مسارات كاسكيد زيادة

                // ٣) ربط اختيارى مع سطر فاتورة البيع (مفتاح مركّب SIId + LineNo)
                e.HasOne(l => l.SalesInvoiceLine)    // SalesReturnLine.SalesInvoiceLine
                 .WithMany()                         // ممكن بعدين نضيف ICollection<SalesReturnLine> فى SalesInvoiceLine لو حبيت
                 .HasForeignKey(l => new { l.SalesInvoiceId, l.SalesInvoiceLineNo })
                 .HasPrincipalKey(li => new { li.SIId, li.LineNo })
                 .OnDelete(DeleteBehavior.Restrict);

                // قيود تحقق على النِّسب والقيم
                e.ToTable(tb =>
                {
                    tb.HasCheckConstraint("CK_SalesReturnLines_Qty", "[Qty] >= 0");
                    tb.HasCheckConstraint(
                        "CK_SalesReturnLines_Percents",
                        "[Disc1Percent] BETWEEN 0 AND 100 AND " +
                        "[Disc2Percent] BETWEEN 0 AND 100 AND " +
                        "[Disc3Percent] BETWEEN 0 AND 100 AND " +
                        "[TaxPercent]   BETWEEN 0 AND 100");
                });
            });





            // ===== SalesOrders (الهيدر) =====


            mb.Entity<SalesOrder>(e =>
            {
                e.ToTable("SalesOrders");              // اسم الجدول في قاعدة البيانات

                // 🔹 المفتاح الأساسي
                e.HasKey(x => x.SOId);

                // 🔹 الخصائص الأساسية
                e.Property(x => x.SOId)
                 .IsRequired();

                e.Property(x => x.CustomerId)
                 .IsRequired();

                e.Property(x => x.WarehouseId)

                 .IsRequired();

                e.Property(x => x.Status)
                 .HasMaxLength(20)
                 .HasDefaultValue("Draft");          // Draft / Converted / Cancelled

                e.Property(x => x.IsConverted)
                 .HasDefaultValue(false);

                e.Property(x => x.CreatedBy)
                 .HasMaxLength(50)
                 .IsRequired();

                // 🔹 علاقة هيدر ← سطور أمر البيع (حذف الهيدر يمسح السطور)
                e.HasMany(x => x.Lines)
                 .WithOne(l => l.SalesOrder)
                 .HasForeignKey(l => l.SOId)
                 .OnDelete(DeleteBehavior.Cascade);

                // 🔹 علاقة أمر البيع مع العميل (Customer)
                // عميل واحد -> له عدة أوامر بيع
                e.HasOne(x => x.Customer)
                 .WithMany(c => c.SalesOrders)        // ICollection<SalesOrder> داخل Customer
                 .HasForeignKey(x => x.CustomerId)    // FK في الهيدر
                 .OnDelete(DeleteBehavior.Restrict);  // لا نحذف الأوامر تلقائياً عند حذف العميل
            });






            // ===== SOLines (سطور أوامر البيع) =====


            mb.Entity<SOLine>(e =>
            {
                e.ToTable("SOLines");   // اسم جدول السطور في الداتا بيز

                // 🔹 المفتاح المركّب: رقم أمر البيع + رقم السطر
                e.HasKey(x => new { x.SOId, x.LineNo });

                // 🔹 خصائص أساسية
                e.Property(x => x.SOId)

                 .IsRequired();

                e.Property(x => x.ProdId)

                 .IsRequired();

                e.Property(x => x.PriceBasis)
                 .HasMaxLength(100);

                e.Property(x => x.PreferredBatchNo)
                 .HasMaxLength(50);

                // الأرقام — EF هيقرأ [Precision] من الموديل، مش محتاج نكررها هنا
                // ممكن تزود HasPrecision هنا لو حابب، بس مش ضروري

                // 🔹 فهارس مفيدة للتقارير
                e.HasIndex(x => x.SOId)
                 .HasDatabaseName("IX_SOL_SOId");

                e.HasIndex(x => x.ProdId)
                 .HasDatabaseName("IX_SOL_ProdId");

                e.Property(x => x.QtyConverted).HasDefaultValue(0);

                // 🔹 العلاقة مع الهيدر SalesOrder
                e.HasOne(x => x.SalesOrder)         // كل سطر يتبع أمر بيع واحد
                 .WithMany(o => o.Lines)            // والأمر له مجموعة سطور
                 .HasForeignKey(x => x.SOId)        // FK في جدول SOLines = SOId
                 .OnDelete(DeleteBehavior.Cascade); // حذف الأمر يمسح سطوره

                e.HasOne(x => x.Product)
                 .WithMany()
                 .HasForeignKey(x => x.ProdId)
                 .OnDelete(DeleteBehavior.Restrict);
            });





            // ===== StockLedger =====


            mb.Entity<StockLedger>(e =>
            {
                e.ToTable("StockLedger");
                e.HasKey(x => x.EntryId);

                e.Property(x => x.EntryId).IsRequired();
                e.Property(x => x.WarehouseId).IsRequired();
                e.Property(x => x.ProdId).IsRequired();
                e.Property(x => x.BatchNo).HasMaxLength(50);
                e.Property(x => x.UnitCost).HasColumnType("decimal(18,4)");
                e.Property(x => x.SourceType).HasMaxLength(30).IsRequired();
                e.Property(x => x.SourceId).IsRequired();
                e.Property(x => x.AdjustmentReason).HasMaxLength(100);
                e.Property(x => x.Note).HasMaxLength(255);

                // فهارس مهمة للبحث و FIFO
                e.HasIndex(x => new { x.WarehouseId, x.ProdId });
                e.HasIndex(x => new { x.WarehouseId, x.ProdId, x.Expiry, x.TranDate, x.EntryId })
                 .HasDatabaseName("IX_StockLedger_Fifo");
                e.HasIndex(x => new { x.SourceType, x.SourceId, x.SourceLine });

                // قيود تحقق (الصيغة الحديثة عبر ToTable(...))
                e.ToTable(tb =>
                {
                    tb.HasCheckConstraint("CK_Stock_Qty_Positive",
                        "[QtyIn] >= 0 AND [QtyOut] >= 0 AND NOT ([QtyIn] > 0 AND [QtyOut] > 0)");
                    tb.HasCheckConstraint("CK_Stock_UnitCost_NonNegative",
                        "[UnitCost] >= 0");
                    tb.HasCheckConstraint("CK_Stock_Remaining_For_Inputs",
                        "([QtyIn] = 0 AND [RemainingQty] IS NULL) OR ([QtyIn] > 0 AND [RemainingQty] >= 0)");
                });
            });





            // ===== StockFifoMap =====
            mb.Entity<StockFifoMap>(e =>
            {
                e.ToTable("StockFifoMap");
                e.HasKey(x => x.MapId);

                e.Property(x => x.MapId).IsRequired();
                e.Property(x => x.OutEntryId).IsRequired();
                e.Property(x => x.InEntryId).IsRequired();
                e.Property(x => x.UnitCost).HasColumnType("decimal(18,4)");

                e.HasIndex(x => x.OutEntryId);
                e.HasIndex(x => x.InEntryId);

                e.ToTable(tb =>
                {
                    tb.HasCheckConstraint("CK_Fifo_Qty_Positive", "[Qty] > 0");
                });
            });






            // =======================
            // جدول التشغيلات Batch
            // =======================
            // علاقة Batch ← Product (كل تشغيلة مرتبطة بصنف)
            mb.Entity<Batch>()
                .HasOne(b => b.Product)
                .WithMany() // لو حبيت تضيف ICollection<Batch> فى Product بعدين تقدر تغيّرها
                .HasForeignKey(b => b.ProdId)
                .OnDelete(DeleteBehavior.Restrict);

            // علاقة StockLedger ← Product
            mb.Entity<StockLedger>()
                .HasOne(sl => sl.Product)
                .WithMany()
                .HasForeignKey(sl => sl.ProdId)
                .OnDelete(DeleteBehavior.Restrict);

            // علاقة StockLedger ← Warehouse
            mb.Entity<StockLedger>()
                .HasOne(sl => sl.Warehouse)
                .WithMany()
                .HasForeignKey(sl => sl.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            mb.Entity<StockLedger>()
                .HasOne(sl => sl.User)
                .WithMany()
                .HasForeignKey(sl => sl.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            // علاقة StockLedger ← Batch (اختيارية)
            mb.Entity<StockLedger>()
                .HasOne(sl => sl.Batch)
                .WithMany()
                .HasForeignKey(sl => sl.BatchId)
                .OnDelete(DeleteBehavior.Restrict);

            // =======================
            // جدول الخصم اليدوي للبيع (ProductDiscountOverrides)
            // =======================
            mb.Entity<ProductDiscountOverride>(e =>
            {
                e.ToTable("ProductDiscountOverrides");
                e.HasIndex(x => x.ProductId).HasDatabaseName("IX_ProductDiscountOverrides_Product");
                e.HasIndex(x => new { x.ProductId, x.WarehouseId, x.BatchId }).HasDatabaseName("IX_ProductDiscountOverrides_ProductWarehouseBatch");
                e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            });

            // ===== موديول برنامج المشتريات =====
            mb.Entity<VendorProductMapping>(e =>
            {
                e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(x => new { x.CustomerId, x.VendorProductCode });
                e.HasIndex(x => new { x.CustomerId, x.VendorProductName });
            });
            mb.Entity<PurchasePolicyRule>(e => { });
            mb.Entity<PurchasingDataSourceConfig>(e => { });
            mb.Entity<VendorFaxUpload>(e =>
            {
                e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                e.HasMany(x => x.Lines).WithOne(x => x.VendorFaxUpload).HasForeignKey(x => x.VendorFaxUploadId).OnDelete(DeleteBehavior.Cascade);
            });
            mb.Entity<VendorFaxLine>(e =>
            {
                e.HasOne(x => x.MatchedProduct).WithMany().HasForeignKey(x => x.MatchedProductId).OnDelete(DeleteBehavior.Restrict);
            });
            mb.Entity<PurchasingOrder>(e =>
            {
                e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.ErpPurchaseRequest).WithMany().HasForeignKey(x => x.ErpPurchaseRequestId).HasPrincipalKey(p => p.PRId).OnDelete(DeleteBehavior.SetNull);
                e.HasMany(x => x.Lines).WithOne(x => x.PurchasingOrder).HasForeignKey(x => x.PurchasingOrderId).OnDelete(DeleteBehavior.Cascade);
            });
            mb.Entity<PurchasingOrderLine>(e =>
            {
                e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            });
            mb.Entity<PurchasingOrderAmendment>(e =>
            {
                e.HasOne(x => x.PurchasingOrder).WithMany().HasForeignKey(x => x.PurchasingOrderId).OnDelete(DeleteBehavior.Cascade);
            });
        }

        




        


        public override int SaveChanges()
        {
            ApplyDefaultBatchAndExpiryValues();
            TrackProductPriceChanges(); // لو اتغيّر PriceRetail نسجل في السجل
            return base.SaveChanges();
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyDefaultBatchAndExpiryValues();
            TrackProductPriceChanges(); // لو اتغيّر PriceRetail نسجل في السجل
            return await base.SaveChangesAsync(cancellationToken);
        }

        private void ApplyDefaultBatchAndExpiryValues()
        {
            // Batch (Expiry غير Nullable)
            foreach (var entry in ChangeTracker.Entries<Batch>().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified))
            {
                var entity = entry.Entity;
                entity.BatchNo = string.IsNullOrWhiteSpace(entity.BatchNo) ? DefaultBatchNo : entity.BatchNo.Trim();
                if (entity.Expiry == default)
                    entity.Expiry = DefaultExpiryDate;
            }

            // الكيانات التي تحتوي Expiry Nullable
            foreach (var entry in ChangeTracker.Entries<StockBatch>().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified))
            {
                var entity = entry.Entity;
                entity.BatchNo = string.IsNullOrWhiteSpace(entity.BatchNo) ? DefaultBatchNo : entity.BatchNo.Trim();
                if (!entity.Expiry.HasValue)
                    entity.Expiry = DefaultExpiryDate;
            }

            foreach (var entry in ChangeTracker.Entries<StockLedger>().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified))
            {
                var entity = entry.Entity;
                entity.BatchNo = string.IsNullOrWhiteSpace(entity.BatchNo) ? DefaultBatchNo : entity.BatchNo.Trim();
                if (!entity.Expiry.HasValue)
                    entity.Expiry = DefaultExpiryDate;
            }

            foreach (var entry in ChangeTracker.Entries<PILine>().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified))
            {
                var entity = entry.Entity;
                entity.BatchNo = string.IsNullOrWhiteSpace(entity.BatchNo) ? DefaultBatchNo : entity.BatchNo.Trim();
                if (!entity.Expiry.HasValue)
                    entity.Expiry = DefaultExpiryDate;
            }

            foreach (var entry in ChangeTracker.Entries<PurchaseReturnLine>().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified))
            {
                var entity = entry.Entity;
                entity.BatchNo = string.IsNullOrWhiteSpace(entity.BatchNo) ? DefaultBatchNo : entity.BatchNo.Trim();
                if (!entity.Expiry.HasValue)
                    entity.Expiry = DefaultExpiryDate;
            }

            foreach (var entry in ChangeTracker.Entries<SalesInvoiceLine>().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified))
            {
                var entity = entry.Entity;
                entity.BatchNo = string.IsNullOrWhiteSpace(entity.BatchNo) ? DefaultBatchNo : entity.BatchNo.Trim();
                if (!entity.Expiry.HasValue)
                    entity.Expiry = DefaultExpiryDate;
            }

            foreach (var entry in ChangeTracker.Entries<SalesReturnLine>().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified))
            {
                var entity = entry.Entity;
                entity.BatchNo = string.IsNullOrWhiteSpace(entity.BatchNo) ? DefaultBatchNo : entity.BatchNo.Trim();
                if (!entity.Expiry.HasValue)
                    entity.Expiry = DefaultExpiryDate;
            }
        }

        // دالة تلتقط أي تعديل على Product.PriceRetail وتضيف صفًا في ProductPriceHistory
        private void TrackProductPriceChanges()
        {
            // اسم المستخدم الحالي (إن لم يوجد نكتب SYSTEM)
            var userName = _httpContextAccessor?.HttpContext?.User?.Identity?.Name ?? "SYSTEM";
            var nowUtc = DateTime.UtcNow;

            // أي Product اتعدّل وحقل PriceRetail متغيّر فعليًا
            var entries = ChangeTracker.Entries<ERP.Models.Product>()
                .Where(e => e.State == EntityState.Modified &&
                            e.Property(p => p.PriceRetail).IsModified);

            foreach (var entry in entries)
            {
                var oldPrice = entry.Property(p => p.PriceRetail).OriginalValue; // قبل
                var newPrice = entry.Property(p => p.PriceRetail).CurrentValue;  // بعد

                if (oldPrice == newPrice) continue; // لا تسجل لو مفيش تغيير فعلي

                // تحديث تاريخ آخر تغيير سعر في كيان المنتج نفسه
                entry.Entity.LastPriceChangeDate = nowUtc;

                // إضافة سجل جديد في جدول التاريخ
                ProductPriceHistories.Add(new ERP.Models.ProductPriceHistory
                {
                    ProdId = entry.Entity.ProdId,
                    OldPrice = oldPrice,
                    NewPrice = newPrice,
                    ChangeDate = nowUtc,
                    ChangedBy = userName,
                    Reason = "تعديل سعر الجمهور" // لاحقًا نقدر نمرّر سبب حقيقي من شاشة التعديل
                });
            }
        }





    }
}
