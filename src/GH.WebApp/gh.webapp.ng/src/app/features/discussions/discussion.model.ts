import { PagedResult } from '../programs/program.model';

export type ThreadKind = 'Question' | 'General';
export type ReportReason = 'Spam' | 'Abuse' | 'OffTopic' | 'Other';
export type ReportStatus = 'Open' | 'Dismissed' | 'Actioned';

export interface Author {
  userId: string;
  displayName: string;
  avatarUrl: string | null;
  /** The program's own trainer. */
  isTrainer: boolean;
}

export interface ThreadSummary {
  id: string;
  kind: ThreadKind;
  title: string;
  author: Author;
  createdAt: string;
  lastActivityAt: string;
  replyCount: number;
  isPinned: boolean;
  isLocked: boolean;
  isHidden: boolean;
  isAnswered: boolean;
}

export interface Board {
  programId: string;
  programTitle: string;
  trainerId: string;
  canPost: boolean;
  canModerate: boolean;
  threads: PagedResult<ThreadSummary>;
}

/** Body is null for deleted posts, and for hidden posts unless the viewer moderates. Author is null for deleted posts. */
export interface Post {
  id: string;
  parentPostId: string | null;
  depth: number;
  author: Author | null;
  body: string | null;
  createdAt: string;
  editedAt: string | null;
  isDeleted: boolean;
  isHidden: boolean;
  isMine: boolean;
  isAnswer: boolean;
}

export interface Thread {
  id: string;
  programId: string;
  programTitle: string;
  kind: ThreadKind;
  title: string;
  author: Author;
  createdAt: string;
  isPinned: boolean;
  isLocked: boolean;
  isHidden: boolean;
  answerPostId: string | null;
  canReply: boolean;
  canModerate: boolean;
  canMarkAnswer: boolean;
  hasMorePosts: boolean;
  /** Flat, in posting order; see {@link buildTree}. */
  posts: Post[];
}

export interface StartThreadRequest {
  kind: ThreadKind;
  title: string;
  body: string;
}

export interface StartedThread {
  id: string;
  openingPostId: string;
}

export interface ModerateThreadRequest {
  pinned?: boolean;
  locked?: boolean;
  hidden?: boolean;
}

export interface Report {
  id: string;
  reason: ReportReason;
  note: string | null;
  status: ReportStatus;
  createdAt: string;
  reporterId: string;
  programId: string;
  threadId: string;
  threadTitle: string;
  postId: string;
  postAuthorId: string;
  postAuthorName: string;
  postBody: string;
  postIsHidden: boolean;
}

export const reportReasons: { value: ReportReason; label: string }[] = [
  { value: 'Spam', label: 'Spam' },
  { value: 'Abuse', label: 'Abusive or harassing' },
  { value: 'OffTopic', label: 'Off topic' },
  { value: 'Other', label: 'Something else' },
];

export interface PostNode {
  post: Post;
  children: PostNode[];
}

/**
 * Turns the API's flat post list into a tree. Roots are posts without a parent (the opening post), plus any post
 * whose parent isn't in the list, so nothing disappears if a thread is truncated. Order within a level is kept.
 */
export function buildTree(posts: Post[]): PostNode[] {
  const nodes = new Map(posts.map((post) => [post.id, { post, children: [] as PostNode[] }]));
  const roots: PostNode[] = [];

  for (const post of posts) {
    const node = nodes.get(post.id)!;
    const parent = post.parentPostId ? nodes.get(post.parentPostId) : undefined;
    (parent ? parent.children : roots).push(node);
  }

  return roots;
}
