using System;                                   // استخدام DateTime للتواريخ
using System.Collections.Generic;               // القوائم List
using System.ComponentModel.DataAnnotations;    // الخصائص مثل Required, StringLength
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// كيان العميل (عميل / مورد / مستثمر / موظف ...)
    /// يحتوي بيانات الاتصال + الربط الجغرافي + حد ائتماني + ربط محاسبي + علاقات مع المستندات.
    /// </summary>
    public class Customer
    {
        // ===== المعرّف الأساسي للطرف =====
        [Key]   // المفتاح الأساسي للجدول (رقم العميل)
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]  // رقم يزيد تلقائياً Identity
        [Display(Name = "كود العميل")]
        public int CustomerId { get; set; }     // متغير: رقم العميل — يُستخدم كـ FK في الجداول الأخرى

        // ===== بيانات التعريف الأساسية =====
        [Required, StringLength(200)]
        [Display(Name = "اسم العميل")]
        public string CustomerName { get; set; } = null!;   // متغير: اسم العميل / المورد / المستثمر / الموظف

        [StringLength(20)]
        [Display(Name = "هاتف 1")]
        public string? Phone1 { get; set; }                 // متغير: رقم الهاتف الأول للطرف

        [StringLength(20)]
        [Display(Name = "هاتف 2")]
        public string? Phone2 { get; set; }                 // متغير: رقم الهاتف الثاني للطرف

        [StringLength(20)]
        [Display(Name = "واتساب")]
        public string? Whatsapp { get; set; }               // متغير: رقم الواتساب للطرف

        [StringLength(250)]
        [Display(Name = "العنوان")]
        public string? Address { get; set; }                // متغير: عنوان العميل

        // ===== فئة العميل / نوع الطرف =====
        [StringLength(50)]
        [Display(Name = "فئة العميل")]
        public string? PartyCategory { get; set; }          // متغير: فئة الطرف (عميل، مورد، موظف، مستثمر ...)

        // ===== الربط الجغرافي (اختياري) =====
        [Display(Name = "المحافظة")]
        public int? GovernorateId { get; set; }             // متغير: معرّف المحافظة (FK اختياري)

        [Display(Name = "الحي / المركز")]
        public int? DistrictId { get; set; }                // متغير: معرّف الحي/المركز (FK اختياري)

        [Display(Name = "المنطقة")]
        public int? AreaId { get; set; }                    // متغير: معرّف المنطقة (FK اختياري)

        [Display(Name = "خط السير")]
        public int? RouteId { get; set; }                    // متغير: خط السير الذي يتبعه العميل

        [ForeignKey(nameof(RouteId))]
        public virtual Route? Route { get; set; }

        // ===== الحالة والملاحظات =====
        [Display(Name = "نشط؟")]
        public bool IsActive { get; set; } = true;          // متغير: هل الطرف نشط أم موقوف عن التعامل

        [StringLength(300)]
        [Display(Name = "ملاحظات")]
        public string? Notes { get; set; }                  // متغير: ملاحظات عامة عن العميل

        // ===== الائتمان والرصيد =====
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "الحد الائتماني")]
        public decimal CreditLimit { get; set; } = 0m;      // متغير: الحد الائتماني المسموح للطرف (غالباً للعميل)

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "زيادة حد ائتماني مؤقتة")]
        public decimal? CreditLimitTemporaryIncrease { get; set; } // متغير: زيادة مؤقتة على الحد الأساسي

        [Display(Name = "انتهاء الزيادة المؤقتة")]
        public DateTime? CreditLimitTemporaryUntil { get; set; }   // متغير: تاريخ/وقت انتهاء الزيادة المؤقتة

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "الرصيد الحالي")]
        public decimal CurrentBalance { get; set; } = 0m;   // متغير: رصيد العميل الحالي (للتقارير/كشوف الحساب)

        // ===== ربط العميل بالحساب المحاسبي (Accounts) =====
        [Display(Name = "معرّف الحساب المحاسبي")]
        public int? AccountId { get; set; }                 // متغير: FK على جدول Accounts (حساب هذا الطرف في شجرة الحسابات)

        [ForeignKey(nameof(AccountId))]
        [Display(Name = "حساب العميل في الدليل المحاسبي")]
        public virtual Account? Account { get; set; }       // متغير: كائن الحساب المحاسبي المرتبط بالطرف

        // ===== التواريخ — نظام القوائم الموحد =====
        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; }             // متغير: تاريخ ووقت إنشاء سجل العميل

        [Display(Name = "تاريخ آخر تعديل")]
        public DateTime UpdatedAt { get; set; }             // متغير: آخر تاريخ ووقت تعديل بيانات العميل

     

        public int? UserId { get; set; }       // المستخدم المسئول عن العميل (السيلز)
        public User? User { get; set; }        // Navigation لجدول Users

        public string? OrderContactName { get; set; }   // اسم المسئول عن الطلب
        public string? OrderContactPhone { get; set; }  // موبايله / واتسابه

        [StringLength(50)]
        [Display(Name = "كود الإكسل")]
        public string? ExternalCode { get; set; }      // مسلسل / كود من ملف الإكسل

        [StringLength(100)]
        [Display(Name = "رقم البطاقة الضريبية / الرقم القومي")]
        public string? TaxIdOrNationalId { get; set; }

        [StringLength(50)]
        [Display(Name = "رقم السجل")]
        public string? RecordNumber { get; set; }

        [StringLength(50)]
        [Display(Name = "رقم الرخصة")]
        public string? LicenseNumber { get; set; }

        [StringLength(50)]
        [Display(Name = "الشريحة")]
        public string? Segment { get; set; }

        [StringLength(50)]
        [Display(Name = "الكود المكاني")]
        public string? LocationCode { get; set; }

        [StringLength(150)]
        [Display(Name = "المنطقة (نص)")]
        public string? RegionName { get; set; }        // منطقة كنص من الإكسل (إن لم تُربط بجدول المناطق)

        public bool IsQuotaMultiplierEnabled { get; set; }   // تفعيل مضاعفة الكوتة
        public int QuotaMultiplier { get; set; } = 1;        // رقم المضاعفة





        // ===== علاقات الملاحة الجغرافية =====
        [ForeignKey(nameof(GovernorateId))]
        public virtual Governorate? Governorate { get; set; }   // متغير: كيان المحافظة

        [ForeignKey(nameof(DistrictId))]
        public virtual District? District { get; set; }         // متغير: كيان الحي/المركز

        [ForeignKey(nameof(AreaId))]
        public virtual Area? Area { get; set; }                 // متغير: كيان المنطقة

        // ===== سياسة العميل (سياسات الأسعار / الائتمان ...) =====
        public int? PolicyId { get; set; }        // متغير: كود السياسة التي يتبعها هذا الطرف (اختياري)

        public Policy? Policy { get; set; }       // متغير: السياسة المرتبطة بهذا الطرف

        // ===== ربط العميل بحسابات الدخول (Users) =====
        [Display(Name = "حسابات الدخول المرتبطة")]
        public virtual ICollection<User> Users { get; set; }
            = new List<User>();                  // متغير: كل المستخدمين المرتبطين بهذا العميل/الموظف/المستثمر...

        [Display(Name = "روابط الموزعين")]
        public virtual ICollection<CustomerCollector> CustomerCollectors { get; set; }
            = new List<CustomerCollector>();     // متغير: كل روابط هذا العميل بالموزعين

        // ===== علاقات العميل مع مستندات البيع =====
        public virtual ICollection<SalesInvoice> SalesInvoices { get; set; }
            = new List<SalesInvoice>();                     // متغير: كل فواتير البيع لهذا الطرف كعميل

        public virtual ICollection<SalesReturn> SalesReturns { get; set; }
            = new List<SalesReturn>();                      // متغير: كل مرتجعات البيع لهذا الطرف

        public virtual ICollection<SalesOrder> SalesOrders { get; set; }
            = new List<SalesOrder>();                       // متغير: كل أوامر البيع لهذا الطرف

        // ===== علاقات العميل مع مستندات المشتريات =====
        public virtual ICollection<PurchaseReturn> PurchaseReturns { get; set; }
            = new List<PurchaseReturn>();                   // متغير: كل مرتجعات الشراء لهذا الطرف (كمورد)

        public virtual ICollection<PurchaseInvoice> PurchaseInvoices { get; set; }
            = new List<PurchaseInvoice>();                  // متغير: كل فواتير الشراء لهذا الطرف (كمورد)

        public virtual ICollection<PurchaseRequest> PurchaseRequests { get; set; }
            = new List<PurchaseRequest>();                  // متغير: كل طلبات الشراء المرتبطة بهذا الطرف
    }
}
