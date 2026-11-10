import { Post, buildTree } from './discussion.model';

const post = (id: string, parentPostId: string | null, depth = 0): Post => ({
  id,
  parentPostId,
  depth,
  author: null,
  body: id,
  createdAt: '2026-11-09T12:00:00Z',
  editedAt: null,
  isDeleted: false,
  isHidden: false,
  isMine: false,
  isAnswer: false,
});

describe('buildTree', () => {
  it('nests replies under their parents in posting order', () => {
    const tree = buildTree([post('open', null), post('a', 'open', 1), post('b', 'open', 1), post('a1', 'a', 2)]);

    expect(tree.map((n) => n.post.id)).toEqual(['open']);
    expect(tree[0].children.map((n) => n.post.id)).toEqual(['a', 'b']);
    expect(tree[0].children[0].children.map((n) => n.post.id)).toEqual(['a1']);
  });

  it('keeps posts whose parent is missing as roots', () => {
    const tree = buildTree([post('open', null), post('orphan', 'gone', 3)]);

    expect(tree.map((n) => n.post.id)).toEqual(['open', 'orphan']);
  });
});
