// Image formats returned by room-info JSON and protobuf viewer events differ.
// Keep both paths identical so owner and sender pictures always have fallbacks.
export function avatarUrls(user = {}) {
 const read = image => {
  if (typeof image === 'string') return [image];
  if (Array.isArray(image)) return image.flatMap(read);
  if (!image || typeof image !== 'object') return [];
  return ['url', 'mUrls', 'urlList', 'url_list', 'urls'].flatMap(key => read(image[key]));
 };
 const candidates = ['avatarThumb', 'avatar_thumb', 'profilePicture', 'profilePictureMedium',
  'avatarMedium', 'avatar_medium', 'profilePictureUrl', 'profilePictureLarge', 'avatarLarge', 'avatar_large'];
 return [...new Set(candidates.flatMap(key => read(user?.[key])))].filter(url => /^https?:\/\//i.test(url))
  .sort((a, b) => Number(/webp/i.test(a)) - Number(/webp/i.test(b)));
}

export function readOwner(info) {
 const owner = info?.data?.owner ?? info?.owner ?? info?.roomInfo?.owner ?? info?.data?.roomInfo?.owner;
 if (!owner) return { ownerNickname: '', ownerAvatars: [] };
 const nickname = [owner.nickname, owner.display_id, owner.displayId, owner.uniqueId, owner.unique_id]
  .find(value => typeof value === 'string' && value.trim());
 return { ownerNickname: nickname?.trim() ?? '', ownerAvatars: avatarUrls(owner).slice(0, 4) };
}
