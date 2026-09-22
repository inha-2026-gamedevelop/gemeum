const el = id => document.getElementById(id);
let ticket = location.hash.slice(1);
history.replaceState(null, '', location.pathname);
const headers = () => ({'Content-Type':'application/json', Authorization:'Bearer '+ticket});
const request = async (path, body) => {
  const r = await fetch(path, {method:'POST', headers:headers(), body:JSON.stringify(body), cache:'no-store', credentials:'omit'});
  const data = await r.json();
  if (!r.ok) throw new Error(typeof data.detail === 'string' ? data.detail : '입력 형식과 요청 횟수를 확인해 주세요.');
  return data;
};
function fields() {
  const a = el('action').value;
  for (const [id, visible] of Object.entries({'login-row':['create','link'].includes(a),'nick-row':['create','nickname'].includes(a),'pass-row':a!=='nickname','new-row':a==='password','confirm-row':['create','password'].includes(a)})) {
    el(id).hidden = !visible;
    for (const input of el(id).querySelectorAll('input')) { input.required = visible; input.disabled = !visible; }
  }
  el('password').minLength = 1;
  el('password').autocomplete = a==='create' ? 'new-password' : 'current-password';
  el('hint').textContent = a==='password' ? '새 비밀번호는 8~15자입니다. 변경하면 기존 로그인 세션이 해제됩니다.' : '새 비밀번호는 8~15자로 설정하세요.';
}
el('action').onchange = fields;
el('form').onsubmit = async event => {
  event.preventDefault();
  const action = el('action').value;
  const nextPassword = el(action==='create'?'password':'new-password').value;
  if (['create','password'].includes(action) && (Array.from(nextPassword).length < 8 || Array.from(nextPassword).length > 15)) { el('result').textContent='새 비밀번호는 8~15자로 설정하세요.'; return; }
  if (['create','password'].includes(action) && el('confirm').value !== el(action==='create'?'password':'new-password').value) { el('result').textContent='비밀번호 확인이 일치하지 않습니다.'; return; }
  el('save').disabled = true;
  try {
    const data = await request('/discord/apply', {action, login_id:el('login').value, nickname:el('nickname').value, password:el('password').value, new_password:el('new-password').value});
    ticket=''; el('form').hidden=true; el('result').textContent=data.message;
    el('profile').textContent=`아이디: ${data.account.login_id} · 닉네임: ${data.account.nickname}`;
  } catch(e) { el('result').textContent=e.message; }
  finally { for (const id of ['password','new-password','confirm']) el(id).value=''; el('save').disabled=false; }
};
(async () => {
  try {
    const data = await request('/discord/context', {});
    const options = data.account ? [['nickname','닉네임 변경'],['password','비밀번호 변경']] : [['create','새 계정 생성'],['link','기존 계정 연결']];
    for (const [value,label] of options) el('action').add(new Option(label,value));
    if (data.account) { el('profile').textContent=`아이디: ${data.account.login_id} · 닉네임: ${data.account.nickname}`; el('nickname').value=data.account.nickname; }
    el('form').hidden=false; el('result').textContent=''; fields();
  } catch(e) { el('result').textContent=e.message; }
})();
