# Bocchi 

我的新的博客系统项目。Since 2025

我们之前把博客改成了Blazor WebAssembly，后来发现不好用，因为没有静态页面，搜索引擎无法爬取。

所以我们再做一遍。


本次项目分为三个部分：

1. 博客前端
2. Home Server
3. Remote Server

现在的设计和传统的博客有了一些变化，我们希望加入一些新的功能：
1. RSS
2. 评论
3. 短文（类似Twitter、Mastodon）
4. ActivityPub（待定）

## Home Server

Home Server是整个博客系统的数据和管理的主系统，这里会存储文章和页面的图文数据，也就是说理论上我们只需要备份Home Server就可以了。

这个部分在我们现在的构想中，他可以部署在局域网、个人PC等不方便直接作为服务器的地方，也没有全天在线的要求，只要我们在编辑和发布文章的时候能够访问到就可以了。

Home Server将包含一个Dashboard，用于管理文章、页面、评论等数据。

（预计Home Server可以做一个包含Remote Server的Api功能，以便我们直接把Home Server部署到公网，但这部分优先级不高。）

<br>

## Remote Server

Remote Server是博客系统中实际在公网中承载访问业务的服务器部分。

我们把WP等传统CMS系统中的Server拆分成了Home Server和Remote Server，主要是为了尽可能精简实际部署在公网上的Server的功能，进而节省成本。

对于博客或者说一个个人主页这样的小型网站，它的实际运维成本越低，越有可能被更多人长期使用。

根据构想，Remote Server应该可以在各种Serverless平台上部署并以最低成本运行。

理论上对于一个单纯的博客来讲，理想情况下绝大多数业务都是静态的，Remote Server只处理极少数不得不用到服务器的业务，比如评论、短文、ActivityPub（待定，优先级靠后）等。

<br>

## 博客前端

这里已静态页面为主，能直接部署在各种静态网站托管服务上，被搜索引擎直接爬取的静态页面，并把不得不动态的部分通过嵌入Web App Component的方式向Remote Server请求。

理论上，Home Server和Remote Server两者相对而言是一体的，共同形成类似于Headless CMS概念的东西。而前端部分可以随便换实现方式。

理论上，可以在Remote Server离线的情况下，公网正常访问博客的基础功能（正常浏览文章，而评论和短文列表等功能不可用）。

<br>

## 大致的功能处理方式

### 博客文章、页面图文

1. 在Home Server的Dashboard上编辑文章、页面（Markdown或其他格式）
2. 保存时，由Home Server解析并预处理（段落拆分、摘要、多媒体处理等）为中间格式。
3. Home Server唤起前端系统的生成器，增量更新静态页面。
4. Home Server将静态页面更新到Github Pages等静态网站托管服务上。

也就是说，博客基础功能部分不需要Remote Server参与。

<br>

本代码仓库中存放的是以.NET为主体的项目主要代码，其他实现将在其他仓库中，如：

- 以Cloudflare Worker为目标平台的Remote Server 
- 使用Vue等前端框架的博客前端