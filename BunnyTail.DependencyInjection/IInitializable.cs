namespace BunnyTail.DependencyInjection;

// 生成後初期化のマーカーインタフェース。コンテナがインスタンスを生成した場合のみ、
// コンストラクタ注入と [Inject] プロパティ注入の後に Initialize が呼ばれる
// (ファクトリ/インスタンス登録はユーザー所有のため対象外。PostConstruct 指定がある場合はそちらが優先)
// Marker interface for post-construction initialization. Initialize is invoked after constructor and [Inject]
// property injection, only for container-constructed instances (factory and instance registrations are
// user-owned and never initialized; an explicit PostConstruct specification takes precedence).
public interface IInitializable
{
    void Initialize();
}
